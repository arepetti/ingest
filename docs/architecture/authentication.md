# Authentication & authorisation

Ingest authenticates every request with a single API key carried in an HTTP header. There is no cookie, no JWT, no OAuth flow. This document explains how the keys are produced, stored, and verified, and how the role model decides who can do what.

## The threat model

We keep things simple:

- API keys are issued by an administrator and handed once to the consumer.
- Plaintext keys are never persisted server-side and never logged after issuance.
- Stolen keys are revocable individually; an account can hold many keys at once for zero-downtime rotation.
- Disabling an account immediately invalidates all of its keys without touching the key rows themselves.

Out of scope: short-lived tokens, mTLS, IP allow-lists. None of these are technically hard to add later (the auth handler is the only place that would change) but the PoC doesn't ship them.

## Anatomy of a key

A plaintext key looks like:

```
abc12345.7N3pK0M9C0LSx0OqGZpY3vW0eFkdsbVz...
```

Two parts separated by a single `.`:

- **`KeyId`** (everything before the dot) — a short, public identifier. Indexed in the database so the verifier can find the matching row with one query.
- **`Secret`** (everything after the dot) — the secret portion. Persisted only as a salted HMAC-SHA256 hash; the cleartext leaves the server exactly once (at generation).

This shape is deliberate: it lets us look up the hash by `KeyId` instead of having to compute hashes for every row in the table.

## What the database stores

For each `ApiKey` row:

| Field        | Meaning |
|--------------|---------|
| `KeyId`      | The public identifier portion. Indexed, unique. |
| `Hash`       | `HMAC-SHA256(pepper, salt ‖ secret)` as hex. |
| `Salt`       | A per-key 16-byte random salt. |
| `AccountId`  | FK to the owning account. |
| `CreatedAt`  | Issuance timestamp. |
| `ExpiresAt`  | Optional absolute expiry. |
| `RevokedAt`  | Set on revoke. |
| `IsDeleted`  | Soft-delete flag (audit retention). |

The HMAC pepper is configured **server-wide** via `ApiKey:Pepper` and stored only in environment/secret stores, never in the database. The salt is per-key. With both in place, leaking the database alone does not let an attacker forge or brute-force a key.

## Generation flow

```
Admin                              Ingest API
  │   POST /api/admin/accounts        │
  │   /{accountId}/keys               │
  │  ─────────────────────────────────►
  │                                   │   IApiKeyHasher.Generate()
  │                                   │     → plaintext, KeyId, Secret, Salt, Hash
  │                                   │
  │                                   │   ApiKeyRepository.AddAsync({KeyId, Hash, Salt, …})
  │                                   │
  │   201 + { plaintext: "..." }      │
  │  ◄─────────────────────────────────
  │
  │ (saves the plaintext somewhere safe)
```

`plaintext` is returned **once**, in the body of the `POST` response. If the caller loses it, the only remedy is to rotate again. The API does not store, log, or transmit it after this point.

## Verification flow

```
Client                              Ingest API
  │   GET /api/whatever               │
  │   X-Api-Key: abc12345.…           │
  │  ─────────────────────────────────►
  │                                   │  ApiKeyAuthenticationHandler:
  │                                   │   1. Split header on '.' → KeyId, Secret
  │                                   │   2. ApiKeyRepository.GetByKeyIdAsync(KeyId)
  │                                   │   3. Check IsActive (not revoked, not expired, not deleted)
  │                                   │   4. hasher.Verify(Secret, row.Salt, row.Hash)  ← constant-time
  │                                   │   5. Load account; reject if disabled/deleted
  │                                   │   6. Emit ClaimsPrincipal:
  │                                   │        NameIdentifier = accountId
  │                                   │        Name           = account.Name
  │                                   │        Role           = "Service" | "Operator" | "Admin"
  │                                   │        ingest:kind    = "User" | "Application"
  │                                   │        ingest:accountLabel = account.Label
  │
  │   200 OK / 401 / 403              │
  │  ◄─────────────────────────────────
```

Each verification touches one index lookup on `apiKeys.KeyId` and one on `accounts._id`. Both are sub-millisecond.

If the header is missing entirely the handler returns `NoResult()` so anonymous endpoints still work. Today only `POST /api/expressions/translate` is anonymous (the SPA's expression-translator helper — see [architecture.md § Live feedback in the admin UI](architecture.md#live-feedback-in-the-admin-ui)). If the header is present but invalid, the handler returns `401` with a `WWW-Authenticate` header.

## Roles

The `Role` claim drives every authorisation decision. Three roles, with progressively wider scope:

| Role       | Intent                                  | Can call |
|------------|-----------------------------------------|----------|
| `Service`  | Automated submitter for one local-council service. | `/api/me*`, `/api/schemas*`, `/api/submissions*`. Reads and writes scoped to its own account. |
| `Operator` | Read-everything back-office user (data analyst). | All `Service` endpoints plus admin **read** endpoints (`/api/admin/submissions` GET, `/api/services/{name}/status`, `/api/admin/query`, `/odata/samples`). |
| `Admin`    | Full control. | Every endpoint, including account/key CRUD, schema CRUD, on-behalf-of submissions, and delete. |

Policies are defined in `Program.cs`:

```csharp
.AddPolicy(AuthConstants.ServicePolicy,  p => p.RequireAuthenticatedUser())
.AddPolicy(AuthConstants.OperatorPolicy, p => p.RequireRole("Operator", "Admin"))
.AddPolicy(AuthConstants.AdminPolicy,    p => p.RequireRole("Admin"));
```

Controllers attach a single policy at the class level; admin-only mutations override it on individual actions.

## Kind

Orthogonal to role:

- **`User`** — interactive credentials for humans. Can log in to the admin SPA. Holding a role of `Service` is unusual but allowed (e.g. a person who only submits data manually).
- **`Application`** — automation credentials. The SPA's login flow rejects them outright with a clear error.

The distinction exists because admin tooling needs a way to enumerate "who can actually log in" without enumerating "who can call the API".

## Single sign-on (optional second scheme)

API keys are always available and are the **only** path for `Application` accounts. On top of them, Ingest can optionally accept **Microsoft / Google SSO** for interactive (`User`) accounts, using a server-side OpenID Connect *code* flow (a "backend-for-frontend", BFF) that issues an `HttpOnly` session cookie. OIDC tokens never reach browser JavaScript.

> **Everything in this section is gated by `Sso:EnableSso`, which defaults to `false`.** With the flag off (the default), no cookie/OIDC scheme is registered, no auth endpoint executes, `GET /api/auth/providers` returns `[]`, and the SPA renders exactly the API-key-only login. None of the `Sso:*` config keys, the "Continue with …" buttons, or the per-account linking field have any effect. Turn the flag on (and configure at least one provider) to light up the rest of this section.

### Why a cookie (and not tokens in the SPA)

The Vite dev server proxies `/api` to the backend, and in production the SPA is served from the API's own `wwwroot`. The SPA and API are therefore **same-origin** in both environments, so a `HttpOnly`, `SameSite=Lax`, `Secure` cookie just works — no cross-site cookie handling, and no access tokens exposed to JS.

### The flow

```mermaid
sequenceDiagram
    participant SPA
    participant API as Ingest API
    participant IdP as Microsoft/Google
    SPA->>API: GET /api/auth/login/{provider}?returnUrl=/
    API-->>SPA: 302 Challenge (OIDC redirect)
    SPA->>IdP: Authorize (code flow + PKCE)
    IdP-->>API: 302 to /api/auth/callback/{provider} (code)
    API->>IdP: Exchange code, validate id_token
    Note over API: OnTokenValidated: match Account by<br/>(provider, verified email); require Kind==User + Enabled<br/>else reject. Rebuild principal with canonical claims.
    API-->>SPA: Set-Cookie ingest.session; 302 returnUrl
    SPA->>API: GET /api/me (cookie)
    API-->>SPA: 200 {id, name, role, kind}
```

### Claims parity

A successful SSO sign-in **rebuilds** the principal with the *exact same* claim set the API-key handler emits — `NameIdentifier`, `Name`, `ingest:accountId`, `ingest:accountName`, `ingest:kind`, `Role`, and the optional `ingest:accountLabel`. Because both schemes produce identical claims, every controller, policy, and the `HttpAuditContext` work unchanged regardless of which scheme authenticated the request.

### Pre-provisioned linking (no auto-provisioning)

SSO never *creates* accounts. An admin first links an external identity to an existing `User` account by recording a **provider + verified email** pair on the account (see the admin user guide). The OIDC callback:

1. reads the `email` (and `sub`) from the validated token,
2. looks up a **live, enabled, `User`-kind** account whose `ExternalLogins` contains a matching `(provider, email)` pair (case-insensitive email),
3. rejects the sign-in (redirect to `/login?sso_error=…`) if there is no match, the account is disabled/deleted, or it is an `Application` account,
4. on first success, binds the provider's `sub` to the link for stable future identification.

The matched account's **role** is what governs access — there is no group-to-role mapping. To revoke SSO access, remove the link or disable the account.

### Multi-scheme policies

When SSO is enabled the three role policies name **both** schemes, so either an API key *or* a session cookie satisfies them:

```csharp
.AddPolicy(AuthConstants.ServicePolicy,  p => p.AddAuthenticationSchemes("ApiKey","IngestSession").RequireAuthenticatedUser())
.AddPolicy(AuthConstants.OperatorPolicy, p => p.AddAuthenticationSchemes("ApiKey","IngestSession").RequireRole("Operator","Admin"))
.AddPolicy(AuthConstants.AdminPolicy,    p => p.AddAuthenticationSchemes("ApiKey","IngestSession").RequireRole("Admin"));
```

When SSO is **off**, the `IngestSession` scheme is never registered, so naming it is a no-op and the policies behave exactly as the API-key-only build.

### Auth endpoints

| Endpoint | Purpose |
|----------|---------|
| `GET /api/auth/providers` | Enabled providers `[{ id, displayName, loginUrl }]` for the SPA's buttons. **Returns `[]` when SSO is off.** |
| `GET /api/auth/login/{provider}?returnUrl=/` | Challenges the provider's OIDC scheme. `returnUrl` is restricted to local paths. 404 when SSO is off / provider unknown. |
| `POST /api/auth/logout` | Clears the session cookie (`SignOut`). No-op when SSO is off. |
| `/api/auth/callback/{provider}` | Owned by the OIDC middleware, not the controller. |

### SSO configuration knobs

All under the `Sso` section; see [docs/setup/configuration.md](../setup/configuration.md) for the full table and the secret-sourcing matrix.

| Key | Default | Notes |
|-----|---------|-------|
| `Sso:EnableSso` | `false` | Master switch. Off → the whole feature is inert. |
| `Sso:CookieName` | `ingest.session` | Name of the session cookie. |
| `Sso:Providers:N:Id` | — | Provider id; drives `/login/{id}` and the scheme name. |
| `Sso:Providers:N:DisplayName` | — | Button label. |
| `Sso:Providers:N:Authority` | — | OIDC issuer URL. |
| `Sso:Providers:N:ClientId` / `ClientSecret` | *(blank)* | **Blank in `appsettings.json` by design** — supplied from a secret source per environment. A provider with a blank id/authority/client id/secret is ignored. |
| `Sso:Providers:N:Scopes` | `openid profile email` | Requested scopes. |

## The bootstrap admin

On first start, `AdminBootstrapper` ensures the system is operable out of the box:

1. Look for an account named `ApiKey:BootstrapAdminName` (default `admin`).
2. If it doesn't exist, create one with kind `User` and role `Admin`.
3. If the account has no active keys, give it one:
   - **If `ApiKey:BootstrapAdminKey` is set**, that exact key is used. Nothing secret is written to the logs — you configured the value, so you already have it. This is the recommended path: set the key in configuration and sign in immediately, no log-scraping required.

     ```
     warn: Bootstrapped admin account 'admin' with the API key from ApiKey:BootstrapAdminKey.
           Present it in the X-Api-Key header. Rotate it via POST /api/admin/accounts/{Id}/keys once you're in.
     ```
   - **If it's empty** (the production default), a random key is generated and written to the logs **once** at `Warning` level:

     ```
     warn: Bootstrapped admin API key (shown only this once): abc123.xyz... .
           Use it in the X-Api-Key header. Set ApiKey:BootstrapAdminKey to avoid this next time,
           or rotate it via POST /api/admin/accounts/{Id}/keys then revoke this one.
     ```

The generated-key path is the only mechanism that ever surfaces a plaintext key in the logs. Whichever path you take, rotate to a fresh key from the SPA/API and revoke the bootstrap one once you're set up — especially if you used a configured key shared with `docker-compose.yml` or a quickstart.

If the admin account already has an active key, the bootstrapper leaves it untouched and logs how to bootstrap another one (point `ApiKey:BootstrapAdminName` at a fresh name, or insert an admin record directly in Mongo). Changing `ApiKey:BootstrapAdminKey` after the first boot has no effect — rotate through the API instead.

## Rotation

Rotation is just "issue a new key, optionally revoke the old one":

```
POST /api/admin/accounts/{id}/keys                  → returns plaintext for the new key
POST /api/admin/accounts/{id}/keys/{keyId}/revoke   → marks the old key revoked
```

Two keys can be active for the same account at the same time, so you can roll out the new value to consumers before retiring the old one. There is no rate limit on the number of active keys; the admin UI listing makes housekeeping straightforward.

## Revocation

`POST /api/admin/accounts/{id}/keys/{keyId}/revoke` is idempotent: revoking an already-revoked key returns 200 with the same row. There is no "unrevoke" — issue a new one instead.

`DELETE /api/admin/accounts/{id}` soft-deletes the account, which causes every subsequent request to fail authentication because the loaded account is marked deleted. The keys remain in the database for audit; they just no longer authenticate anyone.

## Configuration knobs

All under the `ApiKey` configuration section (`appsettings.json`, env vars, or user-secrets):

| Key                          | Default                  | Notes |
|------------------------------|--------------------------|-------|
| `ApiKey:HeaderName`          | `X-Api-Key`              | Header carried by clients. |
| `ApiKey:Pepper`              | `change-me-in-prod`      | **Set this in production.** Server-wide HMAC pepper. Rotating it invalidates every existing key — only do it during a planned migration. |
| `ApiKey:BootstrapAdminName`  | `admin`                  | Name of the auto-bootstrapped admin account. Set to a fresh value if you've lost access to the existing admin and want to bootstrap a new one. |
| `ApiKey:BootstrapAdminKey`   | *(empty)*                | Optional plaintext key (`{keyId}.{secret}`) assigned to the bootstrap admin on first start so you don't have to read it from the logs. Empty → a random key is generated and logged once. See [§ The bootstrap admin](#the-bootstrap-admin). |

## Frequently-asked questions

**Why not OAuth/JWT?**
For a single backend with thick clients (cron jobs, scripts) and one front-end, an API key is much easier to operate. If you grow into multi-tenancy or need delegated authorization, the auth handler is the natural seam to swap.

**How do I rotate the pepper?**
Generate fresh keys for every active account first, then redeploy with the new pepper, then revoke the old keys. There is no automatic migration path.

**Can I have one user with multiple keys?**
Yes — any number. The most common case is during rotation, but you can also issue per-environment keys to the same automation account.

**What is `ingest:kind` for, in the claims?**
The SPA reads it on the login flow to refuse `Application`-kind credentials at the boundary (so admins don't accidentally try to "use the service's key to log in"). Server-side it's not used for authorisation today, but it's there for future per-kind policies.

**How do I integrate Azure AD / Entra ID (or Google)?**
Use the optional [single sign-on](#single-sign-on-optional-second-scheme) path: set `Sso:EnableSso=true`, configure the provider's `Authority`/`ClientId`/`ClientSecret`, register the redirect URI `…/api/auth/callback/{provider}` with the IdP, then link each user's verified email to a `User`-kind account. SSO is a second authentication scheme that emits the same claims as the API-key path, so the existing role model applies unchanged.

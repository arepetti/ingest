# For service clients

You're here because you submit data to Ingest on behalf of a local-council service — automated from a script, scheduler, or any other tool that can make an HTTP call. This folder is your starting point.

| Page                      | What's inside                                                                                                  |
|---------------------------|----------------------------------------------------------------------------------------------------------------|
| [api.md](api.md)          | Full reference for the service-facing API: endpoints, request/response shapes, status codes, validation errors, warnings. |

## Who you are

The administrator running the deployment will have created a **Service-role account** for your service. That account belongs to your team and is identified by a stable machine-style name (e.g. `roads-team`, `waste-collection-north`). All the data you submit ends up attached to that account; everything you read back is scoped to it.

If the deployment uses both kinds of accounts, yours will almost certainly be of kind **Application** — an automation credential that can call the API but can't sign in to the admin SPA. Some services prefer a **User**-kind credential so a person on the team can also manually enter the occasional sample through the web UI; both are technically the same on the API side.

## How to get an API key

You don't generate it yourself. The flow is:

1. Ask your Ingest administrator for an API key against your service's account. They'll need to know your service's name (e.g. `roads-team`) — that uniquely identifies which account to attach the key to.
2. The admin generates a new key in the SPA. The plaintext is shown **once**, in a modal dialog; the admin copies it and sends it to you through a secure channel (password manager share, vault entry, encrypted message — never plain email, never chat).
3. You store it somewhere your automation can read at startup. A typical pattern is a secret in your CI/CD provider, an environment variable on the machine, or a slot in your existing secrets manager.

The format is `KeyId.Secret` separated by a single `.` — for example `abc12345.7N3pK0M9C0LSx0OqGZpY3vW0eFkdsbVz...`. Treat the whole string as opaque: don't parse it.

> If the key is ever lost, leaked, or might be compromised, ask the admin to **revoke** it and issue a new one. The admin can keep both keys live during the handover so your automation never sees a 401 (see [Rotation](#rotation) below).

For the full lifecycle (how keys are stored server-side, how revocation works, how rotation pairs up two keys), see [../architecture/authentication.md](../architecture/authentication.md). You don't strictly need it to *use* the API, but it explains exactly what happens with your key on the other side.

## How you use it

Every request adds a single HTTP header:

```
X-Api-Key: abc12345.7N3pK0M9C0LSx0OqGZpY3vW0eFkdsbVz...
```

That's the entire auth model. No cookies, no OAuth flow, no token refresh.

### First call — confirm the key works

```bash
curl -s -o /dev/null -w "%{http_code}\n" \
  -H "X-Api-Key: $INGEST_KEY" \
  https://ingest.example.org/api/me
# 200 → all good
```

`GET /api/me` returns your account identity (`name`, `label`, `role`, `kind`). It's a great smoke test before doing real work and the cheapest way to validate "is this key still alive?".

### Discover what you can submit

`GET /api/schemas` lists every schema visible to your service — both *global* schemas everyone shares and any restricted to your service explicitly. Each schema details its values, types, cadences, and constraints. See [api.md § `GET /api/schemas`](api.md#get-apischemas) for the full shape.

### Submit data

`POST /api/submissions` posts one batch of samples. All samples in one call must belong to the same schema. The response carries the new submission `id` and any non-blocking `warnings` the server produced. See [api.md § `POST /api/submissions`](api.md#post-apisubmissions) for examples and the error shape.

### Check that the bucket is "satisfied"

`GET /api/me/status` returns, per value and cadence bucket, whether you've already submitted in the current period. Useful as the final step of a scheduled job to assert "yes, this period is closed out". See [api.md § `GET /api/me/status`](api.md#get-apimestatus).

## What you can do via the admin UI

If your account is **User**-kind, you can also sign in to the admin SPA at the deployment URL and:

- Browse your own submissions (filtered to your account).
- Create or edit a submission through a form — useful for one-off corrections or when your automation is down. Same form admins use; the back-end treats it exactly like a `POST /api/submissions`.

Accounts with no back-office capabilities (a typical **Service**) see a slimmed-down sidebar: just **Dashboard** and **Submissions**. Accounts, Schemas and the other sections appear only for accounts granted the matching capabilities.

If your account is **Application**-kind, the SPA's login screen rejects your key with a clear message: only User-kind credentials can sign in. Use the API for everything in that case.

The full walkthrough lives in [../admin-user-guide/README.md § Services console](../admin-user-guide/README.md#services-console-service-role-users).

## Rotation

When the admin issues a replacement key, both keys are valid at the same time. The recommended handover:

1. Receive the new key from the admin.
2. Roll it out to your automation (deploy with the new value).
3. Confirm successful auth from the new key (`GET /api/me` returns 200).
4. Tell the admin it's safe to revoke the old key.

If you skip step 3 the admin might revoke the old key before your automation picks up the new one and you'll get 401s. Take your time.

## When something fails

- **401 Unauthorized** — the key is wrong, was revoked, or your account was disabled. Confirm with the admin; the SPA's **Manage keys** drawer shows the current state of each key.
- **403 Forbidden** — the key is fine, but the action isn't allowed. The two common reasons are: trying to read/write data belonging to another service, or trying to edit a submission whose cadence window has closed.
- **404 Not Found** — usually means the schema you referenced isn't visible to you, or the submission you asked for doesn't exist (or belongs to somebody else).
- **400 Bad Request** — validation failed; the response body lists every rule that didn't pass.

Status codes per endpoint are documented in detail in [api.md § Common status codes](api.md#common-status-codes).

## Where to go next

- [api.md](api.md) — the reference. Endpoint by endpoint.
- [../admin-user-guide/validation.md](../admin-user-guide/validation.md) — if you author your own schemas (some service teams do): how the validation expressions work and what errors look like to your callers.
- [../architecture/authentication.md](../architecture/authentication.md) — the wire-level contract for keys; useful background when you're integrating with a secrets manager or CI pipeline.

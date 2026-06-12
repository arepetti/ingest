# Configuration reference

Every configurable setting Ingest reads, in one place. The same keys work in `appsettings.json`, environment variables, user-secrets, or any other ASP.NET Core configuration provider.

## How configuration is sourced

Ingest is a plain ASP.NET Core app, so the standard precedence applies (later sources override earlier ones):

1. `appsettings.json` and `appsettings.{Environment}.json` shipped with the app.
2. User secrets (`Development` only — see `dotnet user-secrets`).
3. Environment variables.
4. Command-line arguments.

When passing nested keys through environment variables, replace `:` with `__` (double underscore). For example `Ingest:EnableSwagger` becomes the env var `Ingest__EnableSwagger`. Both forms reach the same value internally.

`ASPNETCORE_ENVIRONMENT` selects which `appsettings.{Environment}.json` is layered on and toggles development-only behaviour (Swagger defaults on, CORS dev origins, user-secrets). The shipped Docker image sets it to `Production`; the Aspire local-dev host runs as `Development`. Leave it at `Production` for any real deployment.

## Connection

| Key                          | Default                                  | Notes |
|------------------------------|------------------------------------------|-------|
| `ConnectionStrings:ingest`   | provided by Aspire in dev                | MongoDB connection string. **Required** in any non-Aspire deployment. Include the database name in the path (`mongodb://host/ingest`). |
| `Mongo:Database`             | `ingest`                                 | Used only as a fallback when the connection string has no path component. |

## API key authentication

| Key                          | Default                  | Notes |
|------------------------------|--------------------------|-------|
| `ApiKey:HeaderName`          | `X-Api-Key`              | HTTP header callers use to present the key. |
| `ApiKey:Pepper`              | `dev-pepper-change-me`   | **Set this to a long random value in production.** Server-wide HMAC pepper that hardens stored key hashes. Rotating it invalidates every existing key — only do so during a planned migration. See [../architecture/authentication.md § Configuration knobs](../architecture/authentication.md#configuration-knobs). |
| `ApiKey:BootstrapAdminName`  | `admin`                  | Name of the account the bootstrapper creates on first start. Change it later only if you want to bootstrap a *second* admin account (e.g. to recover from a lost key). |
| `ApiKey:BootstrapAdminKey`   | *(empty)*                | Plaintext key (`{keyId}.{secret}`, e.g. `localdev.local-dev-admin-key-change-me`) assigned to the bootstrap admin on first start, so you don't have to read it from the logs. **When empty (the production default), the app generates a random key and logs it once.** Set this to a long, unique value if you use it — anyone who knows it has admin access until you rotate it. Changing it after the admin already has a key has no effect (rotate via the SPA/API instead). |

## Single sign-on (SSO)

> **The entire `Sso` section only takes effect when `Sso:EnableSso` is `true`.** It is `false` by default, and with it off none of the keys below have any effect, `GET /api/auth/providers` returns `[]`, and the login screen is the API-key-only form. SSO is an *addition* to API keys — API keys keep working regardless.

| Key                              | Default            | Notes |
|----------------------------------|--------------------|-------|
| `Sso:EnableSso`                  | `false`            | Master switch. Off → the feature is completely inert (no cookie/OIDC schemes registered). |
| `Sso:CookieName`                 | `ingest.session`   | Name of the `HttpOnly` session cookie issued after a successful SSO login. |
| `Sso:Providers:N:Id`             | —                  | Stable provider id; drives the route `/api/auth/login/{Id}` and the callback `/api/auth/callback/{Id}`. E.g. `Microsoft`, `Google`. |
| `Sso:Providers:N:DisplayName`    | falls back to `Id` | Label on the SPA's "Continue with …" button. |
| `Sso:Providers:N:Authority`      | —                  | OIDC issuer. Microsoft: `https://login.microsoftonline.com/<tenant>/v2.0`. Google: `https://accounts.google.com`. |
| `Sso:Providers:N:ClientId`       | *(blank)*          | OAuth client id. **Blank in `appsettings.json` by design** — supply it from a secret source (see below). |
| `Sso:Providers:N:ClientSecret`   | *(blank)*          | OAuth client secret. **Blank in `appsettings.json` by design** — supply it from a secret source (see below). |
| `Sso:Providers:N:Scopes`         | `openid profile email` | Scopes requested at the authorize endpoint. The default is the minimum needed to resolve a verified email. |

`appsettings.json` ships the *structure* (provider ids, display names, authorities) with **blank** `ClientId`/`ClientSecret` and `EnableSso:false`. A provider whose id/authority/client-id/secret aren't all filled is ignored, so a half-filled config can't accidentally light up. The `N` is the **zero-based array index**; in env-var form the `:` becomes `__`, so `Sso:Providers:0:ClientId` is `Sso__Providers__0__ClientId`.

### Where the client id / secret come from

`ClientId`/`ClientSecret` are never committed. Layer them in per environment using the standard precedence:

| Environment | How to supply the secrets |
|-------------|----------------------------|
| **Local dev (Aspire)** | Set `Sso:EnableSso=true` in the **AppHost's** configuration and add the parameters `dotnet user-secrets set Parameters:MicrosoftClientId <id>` / `Parameters:MicrosoftClientSecret <secret>` in `src/Ingest.AppHost`. The AppHost projects them onto the API as `Sso__EnableSso` / `Sso__Providers__0__ClientId` / `Sso__Providers__0__ClientSecret` (see `AppHost.cs`). The non-secret provider shape stays in the API's `appsettings.json`. |
| **Local dev (no Aspire)** | `dotnet user-secrets` on `Ingest.Api`: `Sso:EnableSso=true`, `Sso:Providers:0:ClientId=…`, `Sso:Providers:0:ClientSecret=…`. |
| **Production** | Env vars or orchestrator secrets, e.g. Azure Container Apps secret refs: `Sso__EnableSso=true`, `Sso__Providers__0__ClientId=secretref:ms-client-id`, `Sso__Providers__0__ClientSecret=secretref:ms-client-secret` (see [hosting.md](hosting.md)). |
| **Docker** | `__`-delimited env vars on the `ingest` service in `docker-compose.yml`; prefer `${MS_CLIENT_SECRET}` interpolation from a git-ignored `.env` file over inline literals. |

### Redirect URIs to register with the IdP

Register the callback URL (`…/api/auth/callback/{provider}`) per environment with the Entra app registration / Google OAuth client:

| Environment | Redirect URI |
|-------------|--------------|
| Local dev (Vite proxy) | `http://localhost:5173/api/auth/callback/Microsoft` |
| Docker eval stack | `http://localhost:8080/api/auth/callback/Microsoft` |
| Production | `https://<host>/api/auth/callback/Microsoft` |

(Use the matching id for other providers, e.g. `.../api/auth/callback/Google`.)

## Email & notifications

> **The entire `Email` section only takes effect when `Email:Enabled` is `true` (the default).** Set it to `false` and the whole feature is inert — no background workers start, the admin **Settings → Email / Email templates / Notifications** sections and the **Audit → Sent emails** tab disappear, the per-account **Send email** action is hidden, and the email/notification endpoints return 404. This mirrors the SSO master-switch pattern.

The **runtime SMTP connection** (host, port, credentials, from-address) is **stored in the database**, not configuration, so admins can change it without a redeploy (**Settings → Email**). Configuration only provides an optional one-time **seed** used when no settings document exists yet. The SMTP password is encrypted at rest using a key derived from `ApiKey:Pepper`, so there is no extra secret to manage.

| Key                            | Default | Notes |
|--------------------------------|---------|-------|
| `Email:Enabled`                | `true`  | Master switch for the email + notifications feature. |
| `Email:Worker:Enabled`         | `true`  | Whether an in-process background service drains the outbox on a timer. Set `false` to drive sending from an external scheduler/service hitting `POST /api/admin/email/drain` instead — so the sender can be split out later without code changes. |
| `Email:Worker:PollSeconds`     | `30`    | How often the in-process drainer wakes up (seconds, floored at 5). |
| `Email:Worker:MaxAttempts`     | `5`     | Delivery attempts before a message is marked permanently `Failed`. |
| `Email:Worker:BatchSize`       | `25`    | Max messages sent per drain pass. |
| `Email:Smtp:Host`              | *(blank)* | **Seed only.** When set and no settings document exists yet, these values bootstrap the database settings on first start. Ignored once settings exist. |
| `Email:Smtp:Port`              | `587`   | Seed SMTP port. |
| `Email:Smtp:UseStartTls`       | `true`  | Seed STARTTLS flag. |
| `Email:Smtp:Username`          | *(blank)* | Seed SMTP username. |
| `Email:Smtp:Password`          | *(blank)* | Seed SMTP password (encrypted before it's written). |
| `Email:Smtp:FromAddress`       | *(blank)* | Seed From address. |
| `Email:Smtp:FromName`          | *(blank)* | Seed From display name. |

> **Notifications** build on top of the email infrastructure. *What* to notify (upcoming / missed / warnings) and *who* receives it is admin data stored in the database (**Settings → Notifications**); only the scheduler cadence is configuration.

| Key                                   | Default | Notes |
|---------------------------------------|---------|-------|
| `Notifications:Scheduler:Enabled`     | `true`  | Whether an in-process scheduler runs the notification job on a timer. Set `false` to drive runs from an external scheduler hitting `POST /api/admin/notifications/run`. |
| `Notifications:Scheduler:PollMinutes` | `15`    | How often the in-process scheduler triggers a run (minutes, floored at 1). |

## Webhooks

> **The entire `Webhooks` section only takes effect when `Webhooks:Enabled` is `true`. It is `false` by default.** When off, the feature is inert — no background worker starts, the admin **Settings → Webhooks** section is hidden, and every `/api/admin/webhooks/*` endpoint returns 404. This mirrors the email/SSO master-switch pattern. See [admin-user-guide/webhooks.md](../admin-user-guide/webhooks.md) for the admin workflow and the delivery/signature contract.

*What* to deliver and *where* (endpoints, subscribed events, per-service filter, signing secret) is admin data stored in the database; configuration only carries the master switch, the worker cadence, and an optional SSRF allow-list. The `window.upcoming` / `window.missed` events are discovered by the **notification scheduler** (`Notifications:Scheduler:*` above), so that job runs whenever a webhook subscribes to them even if the matching email trigger is off.

| Key                              | Default | Notes |
|----------------------------------|---------|-------|
| `Webhooks:Enabled`               | `false` | Master switch for the outbound webhooks feature. |
| `Webhooks:RequestTimeoutSeconds` | `10`    | Per-attempt HTTP timeout for a delivery POST. |
| `Webhooks:AllowedHostSuffixes`   | `[]`    | Optional SSRF allow-list. When non-empty, an endpoint URL is only delivered if its host ends with one of these suffixes (e.g. `example.org`). Empty = allow any host. |
| `Webhooks:Worker:Enabled`        | `true`  | Whether an in-process background service drains the delivery outbox on a timer. Set `false` to drive sending from an external scheduler hitting `POST /api/admin/webhooks/drain`. |
| `Webhooks:Worker:PollSeconds`    | `15`    | How often the in-process drainer wakes up (seconds). |
| `Webhooks:Worker:MaxAttempts`    | `6`     | Delivery attempts (with exponential backoff) before a delivery is marked permanently `Failed`. |
| `Webhooks:Worker:BatchSize`      | `25`    | Max deliveries sent per drain pass. |

> **Retention** is the time-based clean-up enforcing GDPR storage limitation. It hard-deletes data once it outlives its window. Off by default; every window is a day count where `0` (or absent) means "keep forever". A manual `POST /api/admin/retention/run` (Admin) works regardless of `Enabled`. See [admin-user-guide/settings.md § Retention](../admin-user-guide/settings.md#retention).

| Key                             | Default | Notes |
|---------------------------------|---------|-------|
| `Retention:Enabled`             | `false` | Master switch. When off, nothing is purged and the worker isn't registered. |
| `Retention:PollHours`           | `24`    | How often the in-process worker runs a purge pass (hours, floored at 1). |
| `Retention:SentEmailsDays`      | `0`     | Days to keep delivered/failed outbox emails (full-body PII). `0` = keep forever. |
| `Retention:AuditLogDays`        | `0`     | Days to keep audit-log entries. `0` = keep forever. |
| `Retention:SoftDeletedDays`     | `0`     | Days to keep soft-deleted rows before hard-deleting them. `0` = keep forever. |
| `Retention:NotificationLogDays` | `0`     | Days to keep notification dedupe markers. `0` = keep forever. |

## Application behaviour

| Key                                | Default                       | Notes |
|------------------------------------|-------------------------------|-------|
| `Ingest:EnableSwagger`             | `true`                        | Set `false` in production. Swagger is mostly useful while integrating; in production the OpenAPI document is normally generated offline and shipped with clients. |
| `Ingest:DefaultStatusPeriod`       | `week`                        | Used by `GET /api/me/status` and `GET /api/services/{name}/status` when no `period` query parameter is passed. One of `day`, `week`, `month`, `year`. |
| `Ingest:CorsDevOrigins`            | `["http://localhost:5173"]`   | Honoured only in the `Development` environment so the Vite dev server can call the API. Safe to leave empty (or absent) in production — the SPA is served from the same origin as the API. |

## Hosting & observability

These knobs are not Ingest-specific; they're the standard ASP.NET Core / OpenTelemetry options the app honours in production. They typically apply only when running outside Aspire.

| Variable                                  | When you want it                                                                                              |
|-------------------------------------------|----------------------------------------------------------------------------------------------------------------|
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`| When the app sits behind a reverse proxy (Azure Container Apps, App Service, Nginx, …) so URL generation respects the public hostname. |
| `OTEL_EXPORTER_OTLP_ENDPOINT`             | Send traces and metrics to your OpenTelemetry collector of choice.                                             |
| `OTEL_EXPORTER_OTLP_PROTOCOL`             | Usually `grpc` or `http/protobuf`, matching the endpoint above.                                                |
| `APPLICATIONINSIGHTS_CONNECTION_STRING`   | Shortcut for sending telemetry directly to Azure Application Insights.                                          |

## How the settings are exposed in code

The strongly-typed options classes that read these sections live in:

| Section     | Type             | Source file                                                |
|-------------|------------------|------------------------------------------------------------|
| `Mongo`     | `MongoOptions`   | `src/Ingest.Infrastructure/Mongo/MongoOptions.cs`          |
| `ApiKey`    | `ApiKeyOptions`  | `src/Ingest.Infrastructure/Security/ApiKeyOptions.cs`      |
| `Ingest`    | `IngestOptions`  | `src/Ingest.Api/Options/IngestOptions.cs`                  |
| `Sso`       | `SsoOptions`     | `src/Ingest.Api/Options/SsoOptions.cs`                     |
| `Email`     | `EmailOptions`         | `src/Ingest.Infrastructure/Email/EmailOptions.cs`         |
| `Notifications` | `NotificationOptions` | `src/Ingest.Infrastructure/Email/NotificationOptions.cs` |

If you add a new option, follow the same pattern: add the property, bind it from `IConfiguration` in `Program.cs`, and document the key here.

## Where to next

- [hosting.md](hosting.md) — step-by-step deployment to Azure that uses these settings end-to-end.
- [../architecture/authentication.md](../architecture/authentication.md) — the rationale behind the `ApiKey:*` keys and what happens when you rotate the pepper.

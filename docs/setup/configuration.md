# Configuration reference

Every configurable setting Ingest reads, in one place. The same keys work in `appsettings.json`, environment variables, user-secrets, or any other ASP.NET Core configuration provider.

## How configuration is sourced

Ingest is a plain ASP.NET Core app, so the standard precedence applies (later sources override earlier ones):

1. `appsettings.json` and `appsettings.{Environment}.json` shipped with the app.
2. User secrets (`Development` only — see `dotnet user-secrets`).
3. Environment variables.
4. Command-line arguments.

When passing nested keys through environment variables, replace `:` with `__`. For example `Ingest:EnableSwagger` becomes the env var `Ingest__EnableSwagger`. Both forms reach the same value internally.

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

If you add a new option, follow the same pattern: add the property, bind it from `IConfiguration` in `Program.cs`, and document the key here.

## Where to next

- [hosting.md](hosting.md) — step-by-step deployment to Azure that uses these settings end-to-end.
- [../architecture/authentication.md](../architecture/authentication.md) — the rationale behind the `ApiKey:*` keys and what happens when you rotate the pepper.

# Architecture

This document explains how Ingest is put together, what each piece is responsible for, and how a request flows through the system. Read this first if you plan to extend the service or just want to understand "why is _that_ in _there_".

## What it is

Ingest is a small data-ingestion backend for local-council KPI submissions. Local-council services authenticate with an API key and POST KPI samples; administrators manage the catalogue of accepted schemas, accounts, and submissions through a React/Fluent UI admin SPA. The same backend exposes an OData feed so PowerBI (and any other generic OData consumer) can read the data directly.

It's a PoC — the goal was a small, extensible foundation that can be productionised rather than a fully hardened, multi-tenant SaaS.

## Birds-eye view

```
                                 ┌─────────────────────┐
                                 │  Aspire AppHost     │   dev orchestration only
                                 │  (MongoDB + Mongo   │
                                 │   Express + API +   │
                                 │   Vite dev server)  │
                                 └─────────┬───────────┘
                                           │
                                           ▼
 ┌─────────────────┐                 ┌─────────────────┐               ┌────────────┐
 │ React Admin SPA │  HTTP (X-Api-   │ Ingest.Api      │  MongoDB.     │  MongoDB   │
 │  (Vite, Fluent) │  Key header)    │  ┌───────────┐  │  Driver       │  (Cosmos / │
 │                 │ ───────────────►│  │Controllers│  │ ─────────────►│   self-    │
 └─────────────────┘                 │  └───────────┘  │               │   hosted)  │
                                     │  ┌───────────┐  │               └────────────┘
 ┌─────────────────┐                 │  │  Services │  │
 │ Service client  │   API key       │  └───────────┘  │
 │ (script, bot,   │  ───────────────►  ┌───────────┐  │
 │  scheduler …)   │                 │  │  Repos    │  │
 └─────────────────┘                 │  └───────────┘  │
                                     └─────────────────┘
                                            ▲
                                            │ OData / REST
                                     ┌──────┴──────┐
                                     │   PowerBI   │
                                     │  dashboards │
                                     └─────────────┘
```

## Solution layout

```
src/
  Ingest.AppHost/         Aspire orchestrator (Mongo + API + admin SPA). Local dev only.
  Ingest.ServiceDefaults/ OpenTelemetry, health checks, resilience defaults.
  Ingest.Api/             ASP.NET Core host: controllers, auth, OData, SPA hosting.
  Ingest.Core/            Pure domain model + abstractions. No I/O, no framework code.
  Ingest.Infrastructure/  Concrete implementations: Mongo repos, hashing, NCalc, services.
web/admin/                React + Vite + Fluent UI admin SPA.
tests/Ingest.Tests/       PoC test suite (happy paths only).
Dockerfile                Multi-stage build: SPA + API into one image.
```

The split is deliberately Clean-Architecture-ish: `Core` knows nothing about Mongo or HTTP; `Infrastructure` depends on `Core` and never the other way round; `Api` depends on both.

### Why a separate `Core` project?

Two reasons:

1. It makes the domain model testable without spinning up a Mongo container.
2. The unit tests reference `Ingest.Core` only — there's never a temptation to "just call the repo from a test".

## The domain model

### Account

Represents anyone (or anything) authenticated by Ingest.

| Field | Meaning |
|-------|---------|
| `Name` | Stable machine-style identifier (e.g. `roads-team`). Unique across all accounts, including soft-deleted ones. |
| `Label` | Friendly name displayed in the UI (e.g. "Roads & Highways team"). |
| `Description` | Free-form notes. |
| `Kind` | `User` (interactive — can log in to the UI) or `Application` (API-only). |
| `Role` | `Service`, `Operator` or `Admin`. See [authentication.md](authentication.md). |
| `Enabled` | When false, every API key for this account is invalid. |

Kind and Role are orthogonal: a `User` can hold any role; an `Application` can hold any role. The UI rejects `Application`-kind logins at the boundary.

### ApiKey

Many-to-one with `Account`. Stores only the **hash** and **salt**, never the plaintext. Each row carries a public `KeyId` (the prefix the client sends before the dot) so authentication can locate the right hash with a single index lookup. See [authentication.md](authentication.md) for the full lifecycle.

### Schema (+ SchemaValue)

A **schema** is a package of related KPI values that a service reports together (think "monthly waste collection report"). A **schema value** is a single KPI inside that package (think "tonnes collected").

```
Schema
├─ Name, Label, Description, Notes
├─ Modifiable, Enabled          ← package-level gates
├─ IsGlobal, ServiceIds         ← audience (global, or restricted to listed accounts)
├─ SubmissionValidations[]      ← NCalc expressions that see ALL values at once
└─ Values[]
   └─ SchemaValue
      ├─ Name, Label, Description, Notes
      ├─ Type (String/Integer/Number/Date/Boolean)
      ├─ Unit
      ├─ Cadence (Daily/Weekly/Fortnightly/Monthly/Quarterly/SemiAnnually/Yearly)
      ├─ Required, Modifiable, Enabled  ← per-value gates
      ├─ Min/Max, MinDate/MaxDate, MinLength/MaxLength, RegexPattern
      ├─ ValueValidation              ← NCalc expression that sees this value
      ├─ Warning                       ← optional non-blocking notice
      └─ EnabledIf, VisibleIf          ← optional conditional-display rules
```

Each value has its **own** cadence: a monthly schema can contain weekly KPIs perfectly fine. The cadence is what the validator uses to enforce "only one submission per period" rules and what `me/status` rolls up against.

### Submission

A submission is the unit of writes: an account sends a batch of `Sample` rows in one request, all referring to the same schema. Submissions are immutable past their cadence window for Service-role callers; Admins can replace them retroactively.

### SampleProjection

A denormalised, one-document-per-sample read model rebuilt on every submission save. It's what the OData feed (`/odata/samples`) and the admin query endpoint (`/api/admin/query`) actually read — submissions themselves are never touched by reporting workloads, so the schema for analysis can evolve independently of the schema for ingestion.

## Request flow

A typical `POST /api/submissions` walks through these layers, in this order:

1. **Authentication handler** (`ApiKeyAuthenticationHandler`) reads `X-Api-Key`, splits it into `KeyId` and `Secret`, looks up the key row, verifies the HMAC, loads the account, then emits a `ClaimsPrincipal` with `accountId`, `name`, `label`, `kind`, and `role` claims.

2. **Authorization policy** (`Service`, `OperatorOrAdmin`, `Admin`) gates the controller action based on the principal's role.

3. **Controller** (`SubmissionsController`) does parameter binding and DTO mapping. No business logic.

4. **Service** (`SubmissionService`) is where everything interesting happens:
   - Resolve the schema by name (using the visibility filter — the caller can't submit to a schema it isn't assigned to).
   - Hand the assembled `Submission` to `ISubmissionValidator`.
   - Persist via `ISubmissionRepository`.
   - Rebuild the `SampleProjection` rows for that submission so the OData feed sees the new data immediately.
   - For replacements only: check the cadence window is still open before letting a Service-role caller through.

5. **Repository** (`SubmissionRepository`, etc.) is a thin Mongo wrapper. It applies the soft-delete filter, deals with paging, and that's about it.

6. **Exception handler** (`ProblemDetailsExceptionHandler`) maps domain exceptions to HTTP status codes:
   - `NotFoundException` → 404
   - `ConflictException` → 409 (e.g. duplicate name)
   - `ForbiddenException` → 403 (e.g. cadence window closed)
   - `ValidationException` → 400 with `errors[]` extension
   - everything else → 500

The split keeps controllers tiny, the service layer easy to unit-test, and the repositories unaware of any business rule.

## Validation

Validation runs inside `SubmissionValidator` and is layered:

1. **Visibility / enabled-state** — schema must be visible to the calling account and not disabled.
2. **Conditional display** — for every sample, `SchemaValue.EnabledIf` and `VisibleIf` are evaluated against the whole submission context. Samples whose rule is false are *discarded* (not persisted) and a warning is added to the response. Subsequent passes ignore discarded samples.
3. **Shape** — type matches; min/max, regex, length constraints honoured.
4. **Per-value NCalc rule** — `SchemaValue.ValueValidation` runs once per surviving sample with `value`, `minimum`, `maximum` in scope.
5. **Warning rules** — `SchemaValue.Warning` runs per surviving sample; a truthy result contributes to the response warnings (non-blocking).
6. **Cadence** — Mongo lookup against `SampleProjection` to ensure no other sample exists for the same `(service, schema, value)` inside the current cadence bucket. On replacement, the submission being replaced is excluded.
7. **Modifiability** — Service-role callers can only change a sample whose value is `Modifiable`.
8. **Schema-level NCalc rules** — `Schema.SubmissionValidations` runs once per schema present in the payload, with every value of the schema exposed as a variable (or `null` if the sample is absent or was discarded).
9. **Required values** — on create, every required value of every schema the submission carries samples for must be present. Schemas the service is assigned to but didn't include in this submission are not checked (a service with N visible schemas can still send one schema at a time). Values whose conditional rule is false are exempt.

Validators may return either a boolean or a string. A non-empty string is treated as an error message and surfaced verbatim, so authors can write nice user-facing messages without code changes. See [../admin-user-guide/validation.md](../admin-user-guide/validation.md) for the admin-facing rule-authoring guide.

### Live feedback in the admin UI

The submission editor previews `EnabledIf` / `VisibleIf` / `Warning` rules as the user types so fields appear, disable themselves, or surface warnings without a round-trip per keystroke. Rather than ship a second parser to the browser, the API exposes an unauthenticated `POST /api/expressions/translate` endpoint backed by `IExpressionTranslator` (implementation: `NCalcToJavaScriptTranslator`). It walks the NCalc AST with `ILogicalExpressionVisitor<string>` and emits a single JavaScript expression that the SPA wraps in `new Function("V", "H", ...)`. The runtime helpers (`H`) live in `web/admin/src/utils/expression.ts` and implement the same null-handling and built-in functions the server-side evaluator uses, so what the user sees in the editor matches what they'd get if they posted the submission.

The target language is selected through standard HTTP content negotiation. The SPA sends `Accept: text/javascript`; the server matches it (or `application/javascript`, `*/*`, `application/*`, `text/*`) and returns the translated expression in the requested media type. Any other Accept value gets a `406 Not Acceptable`. The shape leaves room for additional targets — a future `text/plain` is reserved for a human-readable explanation of the rule — without changing the route or the request body.

Translations are deterministic functions of the source string, so the client caches them by source for the lifetime of the page; each unique rule is translated exactly once. Server-side validation remains authoritative — if translation fails or a rule references something the runtime can't resolve, the editor stays permissive (show + no warning) and the real verdict comes back with the submission response.

## Cadence semantics

`CadenceCalculator.BucketFor(cadence, timestamp)` returns the half-open `[start, end)` window that contains `timestamp` for the given cadence:

| Cadence       | Bucket                                                                                                                                                                                                                |
|---------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Daily         | 00:00:00 of the day → 00:00:00 of the next day (UTC).                                                                                                                                                                 |
| Weekly        | ISO-style Monday-anchored week.                                                                                                                                                                                       |
| Fortnightly   | 14-day window, Monday-anchored. Aligned to a fixed reference Monday (2001-01-01) so consecutive fortnights never overlap and every service sees the same biweek boundaries regardless of when the schema was created. |
| Monthly       | First of the month 00:00 → first of next month 00:00.                                                                                                                                                                 |
| Quarterly     | Calendar quarter: Q1 = Jan–Mar, Q2 = Apr–Jun, Q3 = Jul–Sep, Q4 = Oct–Dec.                                                                                                                                             |
| SemiAnnually  | Calendar half-year: H1 = Jan–Jun, H2 = Jul–Dec.                                                                                                                                                                       |
| Yearly        | January 1st 00:00 → next January 1st 00:00.                                                                                                                                                                           |

The validator uses this for cadence checks; the status service uses it for "satisfied this period?" computations; the history endpoint uses it for chart buckets.

## Aspire (`Ingest.AppHost`)

The AppHost is the dev-time orchestrator only. It declares:

- A MongoDB container (`AddMongoDB`), with a named data volume so data survives restarts.
- Mongo Express alongside, for quick database inspection.
- The API project, with a reference to the Mongo database (Aspire wires the connection string).
- The Vite dev server (`AddNpmApp`) with the API endpoint passed in via `VITE_API_URL`.

In production, the Docker image bundles the compiled API and the built SPA into a single container — Aspire isn't in the picture.

## Configuration

Configuration is pure ASP.NET Core: `appsettings.json`, environment variables, user-secrets. Mapped to strongly-typed options:

| Section     | Type             | Source file                              |
|-------------|------------------|------------------------------------------|
| `Mongo`     | `MongoOptions`   | `Ingest.Infrastructure/Mongo/MongoOptions.cs` |
| `ApiKey`    | `ApiKeyOptions`  | `Ingest.Infrastructure/Security/ApiKeyOptions.cs` |
| `Ingest`    | `IngestOptions`  | `Ingest.Api/Options/IngestOptions.cs`    |

See [../setup/configuration.md](../setup/configuration.md) for the full table of keys and defaults.

## Mongo indexes

Indexes are ensured on startup by `MongoSetup.EnsureIndexesAsync`:

| Collection    | Index                                                                    | Purpose |
|---------------|--------------------------------------------------------------------------|---------|
| `accounts`    | `uniq_name` on `Name` (unique)                                           | Name uniqueness + fast lookup. |
| `apiKeys`     | `uniq_keyId` on `KeyId` (unique), `by_account` on `AccountId`            | Auth lookup, admin listing. |
| `schemas`     | `uniq_name` on `Name` (unique)                                           | Name uniqueness + fast lookup. |
| `submissions` | `by_service_time` on `(ServiceAccountId, SubmittedAt desc)`              | Per-service listing. |
| `samples`     | `by_service_schema_value_time` on `(ServiceAccountId, SchemaName, ValueName, Timestamp desc)`<br/>`by_service_schema_value_period` on `(…, PeriodStart)`<br/>`by_submission` on `SubmissionId` | Status queries, history aggregation, cascade-delete on submission removal. |
| `reports`     | `uniq_name` on `Name` (unique)                                           | Name uniqueness + fast lookup for the report viewer. |

## Reports

`Report` documents are HTML+Liquid templates uploaded by admins and stored verbatim alongside their parsed YAML front-matter metadata (`Name`, `Label`, `Description`, `Type`, `TargetSchemaNames`). Rendering is server-side via [Fluid](https://github.com/sebastienros/fluid) running with an `UnsafeMemberAccessStrategy` so templates can reach into the curated data envelope (schema, services, value buckets, samples) without per-type registration. The envelope shape depends on `ReportType`: `Single` carries one submission and the owning schema/service; `Aggregate` carries the per-value bucketed history of a schema over a date range. Render output is dropped into a `sandbox=""` iframe in the SPA so any script/style hostility in the template is neutralised. See [admin-user-guide/reports.md](../admin-user-guide/reports.md) for the author-facing reference.

## Testing

The PoC test project (`tests/Ingest.Tests`) covers happy paths only:

- API-key hashing roundtrip.
- NCalc evaluator semantics (boolean, string-message, null-safe).
- NCalc → JavaScript translation (short-circuit, identifiers, function calls).
- Cadence bucketing.
- The submission service end-to-end with an in-memory fake repository.

Repository-level integration tests would require a real Mongo and were intentionally skipped at this stage.

## Further reading

- [authentication.md](authentication.md) — how API keys work end-to-end.
- [../client/api.md](../client/api.md) — full reference for the service-facing API surface.
- [../admin-user-guide/README.md](../admin-user-guide/README.md) — how to operate the system from the admin SPA.
- [../admin-user-guide/validation.md](../admin-user-guide/validation.md) — writing validation rules at sample and schema level.
- [../setup/hosting.md](../setup/hosting.md) — deploying to Azure.
- [../setup/powerbi.md](../setup/powerbi.md) — connecting reporting tools.

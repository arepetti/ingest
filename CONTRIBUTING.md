# Contributing to Ingest

Welcome! This document covers what you need to know to get a working dev environment, run the test suite, and find your way around the codebase. If you're after the product introduction, see [`README.md`](README.md). If you're after how the system is *designed*, the [architecture docs](docs/architecture/README.md) are the place to start.

## Prerequisites

- **.NET 10 SDK** (the API, services, tests, and Aspire orchestrator all target `net10.0`).
- **Node.js 22+** and **npm 11+** (the admin SPA is a Vite + React + TypeScript app).
- **Docker** — Aspire spins up a local MongoDB container for you; you don't have to install Mongo yourself.

That's it. No global npm packages, no `dotnet tool install`, no Mongo installation.

## Repository layout

```
src/
  Ingest.AppHost/         Aspire orchestrator (Mongo + API + admin SPA). Local dev only.
  Ingest.ServiceDefaults/ OpenTelemetry, health checks, resilience defaults.
  Ingest.Api/             REST API, OData, auth, SPA host.
  Ingest.Core/            Pure domain model + abstractions. No I/O, no framework.
  Ingest.Infrastructure/  Mongo repos, hashing, NCalc, services.
web/admin/                React + Vite + Fluent UI admin SPA.
tests/Ingest.Tests/       PoC test suite (happy paths only).
docs/                     Long-form documentation. Index: docs/README.md.
Dockerfile                Multi-stage build: SPA + API into a single image.
```

The split is deliberately Clean-Architecture-ish: `Core` knows nothing about Mongo or HTTP, `Infrastructure` depends on `Core` and never the other way around, `Api` depends on both. Full discussion in [docs/architecture/architecture.md § Solution layout](docs/architecture/architecture.md#solution-layout).

## First time

Install the SPA's dependencies so the Aspire host can boot the Vite dev server:

```powershell
npm install --prefix web/admin
```

That's the only one-time setup step.

## Running locally (recommended: Aspire)

A single command boots everything — MongoDB, Mongo Express, the API, and the Vite dev server:

```powershell
dotnet run --project src/Ingest.AppHost
```

The Aspire dashboard URL is printed in the console; the `admin` resource there links to <http://localhost:5173>.

On first start, look for a `Warning`-level log line from the API:

```
warn: Bootstrapped admin API key (shown only this once): abc123.xyz... .
      Use it in the X-Api-Key header.
```

Copy that value — you only get to see it once. Paste it on the SPA's login screen, then immediately [rotate it](docs/architecture/authentication.md#rotation) and revoke the bootstrap key.

### Running the SPA standalone (without Aspire)

Sometimes you want to iterate on the SPA against an existing API. Skip Aspire:

```powershell
cd web/admin
npm install
npm run dev
```

When run outside Aspire, the Vite proxy falls back to `http://localhost:5000`. Set `VITE_API_URL` to override.

## Tests

```powershell
dotnet test
```

The PoC suite covers happy paths only:

- API-key hashing roundtrip.
- NCalc evaluator semantics (boolean, string-message, null-safe).
- NCalc → JavaScript translation (short-circuit, identifiers, function calls).
- Cadence bucketing.
- The submission service end-to-end with an in-memory fake repository.

Repository-level integration tests would require a real Mongo and were intentionally skipped at this stage.

## Building the production image

The Dockerfile bundles the compiled API and the built admin SPA into a single image:

```powershell
docker build -t ingest .
docker run -p 8080:8080 `
  -e ConnectionStrings__ingest="mongodb://host.docker.internal:27017/ingest" `
  -e ApiKey__Pepper="please-change-me" `
  ingest
```

For real deployments (Azure Container Apps, App Service, AKS, self-hosted) see [docs/setup/hosting.md](docs/setup/hosting.md). For the full set of environment variables you can pass, see [docs/setup/configuration.md](docs/setup/configuration.md).

## Swagger / OpenAPI

The live Swagger UI is exposed at `/swagger` while in `Development` or whenever `Ingest:EnableSwagger=true`. It's the authoritative API reference — XML documentation comments on every endpoint flow into it.

The OpenAPI document is also available at `/swagger/v1/swagger.json`; feed it to your client generator of choice.

## Where to read about the design

- [docs/architecture/architecture.md](docs/architecture/architecture.md) — solution layout, domain model, request flow, validation pipeline, cadence semantics, Aspire orchestration, Mongo indexes, testing strategy.
- [docs/architecture/authentication.md](docs/architecture/authentication.md) — API-key lifecycle, role/kind model, bootstrap admin.
- [docs/admin-user-guide/validation.md](docs/admin-user-guide/validation.md) — rule-authoring reference (the same rules the validator parses with NCalc).

## Submitting changes

This is a PoC — branching strategy and PR review process aren't formalised. Keep changes small and focused; if a change touches the public API or the admin SPA, update the relevant doc under `docs/` in the same change.

Before pushing:

- `dotnet build` is green.
- `dotnet test` passes.
- `npm run build --prefix web/admin` succeeds.
- New public types carry XML documentation comments (the project is set up to fail the build otherwise).

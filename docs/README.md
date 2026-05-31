# Ingest documentation

This folder collects the long-form documentation for **Ingest**, the small data-ingestion backend for local-council KPI submissions. The repository's top-level [`README.md`](../README.md) carries the quick-start; everything else lives in the four sub-folders below.

## Pick your starting point

### "I just want to try it"

Run the whole stack locally with **only Docker** (no .NET SDK, Node, or MongoDB) via [**setup/quickstart.md**](setup/quickstart.md). It's the fastest way to see Ingest end-to-end before committing to a deployment.

### "I run a deployment / I manage accounts and schemas"

Start with [**admin-user-guide/**](admin-user-guide/README.md). It splits into focused pages — accounts, schemas, submissions, validation rules, troubleshooting — so you can jump straight to the task at hand. Keep [architecture/authentication.md](architecture/authentication.md) handy for the key-lifecycle details.

### "I'm a developer integrating against the API"

Read [**client/**](client/README.md). The README explains how to obtain a key and what the workflow looks like; [client/api.md](client/api.md) is the full endpoint reference with status codes, request/response examples and validation behaviour.

### "I'm a data analyst / PowerBI report author"

Go to [**setup/powerbi.md**](setup/powerbi.md). It covers the OData feed, sample queries, and Power Query gotchas. You'll need an Operator-role key from your admin first.

### "I deploy the service"

[**setup/hosting.md**](setup/hosting.md) walks through an Azure Container Apps + Cosmos DB for MongoDB (vCore) deployment step by step, with alternatives for App Service, AKS, and self-hosted MongoDB. The companion [**setup/configuration.md**](setup/configuration.md) is the reference for every setting the app reads.

### "I'm a maintainer / contributor"

Start with [**../CONTRIBUTING.md**](../CONTRIBUTING.md) for the dev environment (prerequisites, running locally with Aspire, tests, building the image). Then [**architecture/**](architecture/README.md) is the source of truth for system design, code layout, and trade-offs. After that the source itself (with the XML docs generated into Swagger) is the next step.

## All sections

| Folder                                                              | What's inside                                                                                  |
|---------------------------------------------------------------------|------------------------------------------------------------------------------------------------|
| [admin-user-guide/](admin-user-guide/README.md)                     | Walkthrough of the admin SPA: accounts, schemas (incl. multi-line validation, `Enabled if` / `Visible if` / `Warning`), submissions, on-behalf-of editing, **reports** (HTML+Liquid templates), validation rule reference, troubleshooting. |
| [architecture/](architecture/README.md)                             | System overview: solution layout, domain model, request flow, validation pipeline, cadence, Mongo, Aspire, configuration, plus the auth model end-to-end. |
| [client/](client/README.md)                                         | Everything a service-side developer needs: how to obtain a key, how to use it, full API reference. |
| [setup/](setup/README.md)                                           | Production deployment to Azure, full configuration reference, plus connecting PowerBI / OData clients. |

## Conventions used across the docs

- **Code references** point at files under `src/` or `web/admin/` from the repository root.
- **Example URLs** use `https://ingest.example.org/` as the placeholder host.
- **Example IDs** are fabricated UUIDs — they're not meaningful, only illustrative.
- **Configuration keys** follow the .NET convention with `:` (e.g. `Ingest:EnableSwagger`). On environment variables that becomes `__` (e.g. `Ingest__EnableSwagger`).

## Where the docs end and the source picks up

- **API request/response shapes** are documented at a representative level in [client/api.md](client/api.md); the live `/swagger` UI on a running deployment is the authoritative reference and is fed by XML comments in the codebase.
- **Domain entities** are described in [architecture/architecture.md § The domain model](architecture/architecture.md#the-domain-model); the actual fields live in `src/Ingest.Core/Entities/`.
- **Validation rules** for schema authors are explained in [admin-user-guide/validation.md](admin-user-guide/validation.md); the pipeline they fit into is described in [architecture/architecture.md § Validation](architecture/architecture.md#validation).

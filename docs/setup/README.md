# Setup & integration

Operations-side documentation: standing up the service and hooking reporting tools onto its data.

| Page                                  | What's inside                                                                                       |
|---------------------------------------|------------------------------------------------------------------------------------------------------|
| [quickstart.md](quickstart.md)        | Run the whole stack locally in ~5 minutes with **only Docker** — no .NET SDK, Node, or MongoDB. The fastest way to evaluate Ingest. |
| [hosting.md](hosting.md)              | Step-by-step Azure deployment (Container Apps + Cosmos DB for MongoDB), including a **free ~$0 evaluation tier** (Container Apps free grant + vCore Free Tier + GHCR), plus alternatives (App Service, AKS, self-hosted MongoDB). Operational checklist. |
| [configuration.md](configuration.md)  | Full reference for every configurable setting — connection string, API-key pepper and header, application behaviour, hosting/observability variables. |
| [powerbi.md](powerbi.md)              | Connecting Power BI (or any OData v4 client) to the `/odata/samples` feed. Custom-header auth recipe, pre-filtering, data-model tips, refresh schedules. |
| [excel.md](excel.md)                  | Connecting Excel (Get & Transform / Power Query) to the `/odata/samples` feed — the cheapest analyst on-ramp using existing Microsoft 365. Header recipe, key-in-parameter, flattening, PivotTables, refresh. |

## Related reading

- [../architecture/authentication.md](../architecture/authentication.md) — the auth knobs hosting.md references (e.g. `ApiKey:Pepper`, bootstrap admin) in detail.
- [../admin-user-guide/README.md](../admin-user-guide/README.md) — once the service is up, this is how you operate it from the SPA.
- [../client/api.md](../client/api.md) — the API a service hits programmatically once it has a key.

# Setup & integration

Operations-side documentation: standing up the service and hooking reporting tools onto its data.

| Page                                  | What's inside                                                                                       |
|---------------------------------------|------------------------------------------------------------------------------------------------------|
| [quickstart.md](quickstart.md)        | Run the whole stack locally in ~5 minutes with **only Docker** — no .NET SDK, Node, or MongoDB. The fastest way to evaluate Ingest. |
| [hosting.md](hosting.md)              | Step-by-step Azure deployment (Container Apps + Cosmos DB for MongoDB), including a **free ~$0 evaluation tier** (Container Apps free grant + vCore Free Tier + GHCR), plus alternatives (App Service, AKS, self-hosted MongoDB). Operational checklist. |
| [configuration.md](configuration.md)  | Full reference for every configurable setting — connection string, API-key pepper and header, application behaviour, hosting/observability variables. |
| [powerbi/](powerbi/README.md)         | Connecting Power BI (or any OData v4 client) to the OData feeds (`/odata/samples` data + `/odata/scorecard` RAG board + `/odata/schemas` metadata catalogue). Custom-header auth recipe, query options, per-feed column references, data-model tips, refresh schedules. |
| [excel.md](excel.md)                  | Connecting Excel (Get & Transform / Power Query) to the `/odata/samples` feed — the cheapest analyst on-ramp using existing Microsoft 365. Header recipe, key-in-parameter, flattening, PivotTables, refresh. |
| [ms-teams.md](ms-teams.md)            | Standing up the **Microsoft Teams** integration: Azure Bot registration, client secret, messaging endpoint, Teams app packaging, and wiring the bot to Ingest. Prerequisites (organizational tenant, public HTTPS), configuration reference, and troubleshooting. |
| [performance.md](performance.md)      | Expected workload, throughput, and response times for a typical council deployment on the standard Azure hosting setup — data volume, QPS, latencies, and when to revisit sizing. |
| [disaster-recovery.md](disaster-recovery.md) | **Starting-point** disaster recovery plan template written against the standard Azure setup — recovery objectives, backup/restore runbooks, failure scenarios, and a customisation checklist. Review and adapt to your regulations and hosting before relying on it. |

## Related reading

- [../architecture/authentication.md](../architecture/authentication.md) — the auth knobs hosting.md references (e.g. `ApiKey:Pepper`, bootstrap admin) in detail.
- [../admin-user-guide/README.md](../admin-user-guide/README.md) — once the service is up, this is how you operate it from the SPA.
- [../client/api.md](../client/api.md) — the API a service hits programmatically once it has a key.

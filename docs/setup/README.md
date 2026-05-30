# Setup & integration

Operations-side documentation: standing up the service and hooking reporting tools onto its data.

| Page                                  | What's inside                                                                                       |
|---------------------------------------|------------------------------------------------------------------------------------------------------|
| [hosting.md](hosting.md)              | Step-by-step Azure deployment (Container Apps + Cosmos DB for MongoDB), plus alternatives (App Service, AKS, self-hosted MongoDB). Operational checklist. |
| [configuration.md](configuration.md)  | Full reference for every configurable setting — connection string, API-key pepper and header, application behaviour, hosting/observability variables. |
| [powerbi.md](powerbi.md)              | Connecting Power BI (or any OData v4 client) to the `/odata/samples` feed. Custom-header auth recipe, pre-filtering, data-model tips, refresh schedules. |

## Related reading

- [../architecture/authentication.md](../architecture/authentication.md) — the auth knobs hosting.md references (e.g. `ApiKey:Pepper`, bootstrap admin) in detail.
- [../admin-user-guide/README.md](../admin-user-guide/README.md) — once the service is up, this is how you operate it from the SPA.
- [../client/api.md](../client/api.md) — the API a service hits programmatically once it has a key.

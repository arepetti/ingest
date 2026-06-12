# Ingest

**A simple way to collect KPI data from local-council services, keep it clean, and feed it straight into your reporting tools.**

Local-council services — waste collection, roads, public health, and the like — produce numbers every day, every week, every month. Getting those numbers into a central database where analysts can query them is usually a tangle of spreadsheets, ad-hoc forms, and email reminders. Ingest replaces that tangle with one small back-end and one web console:

- **Services** push their KPIs through a stable API (or type them in by hand if they prefer).
- **Administrators** define which KPIs are expected, what they should look like, and when they're due — all without writing code.
- **Analysts** read everything through a single OData feed that Power BI talks to out of the box.

It's deliberately small. One container, one database. No multi-tenant SaaS, no microservice mesh — just enough machinery to run a real KPI catalogue end-to-end.

## What you get

### A catalogue services have to respect

Administrators define **schemas** — packages of KPI values (think *"monthly waste collection report"* with `tonnes_collected`, `incidents`, `downtime_hours`). Each value has a type, a unit, a reporting cadence, and a flag for whether it's required. Services see exactly the schemas they're entitled to submit against; everyone else sees nothing.

### Data that's clean before it lands

Every submission is validated server-side:

- **Shape checks** — types match, numbers fall within min/max, strings honour length and regex constraints.
- **Custom rules** — admins can attach business rules in a small expression language (e.g. *"expenses cannot exceed revenue"*, *"this value can only be reported on a weekday"*) without touching the source code. Rules can return either a plain yes/no or a friendly error message that's shown verbatim to the submitter.
- **Conditional fields** — values that only make sense in certain combinations can be hidden or greyed out automatically (e.g. *"only ask for incident notes when there was at least one incident"*).
- **Soft warnings** — schemas can flag unusual-but-legal values so analysts notice them, without rejecting the data.
- **Cadence enforcement** — at most one submission per period, per service, per KPI. Supported cadences are **daily**, **weekly**, **fortnightly** (Monday-anchored biweeks), **monthly**, **quarterly** (calendar quarters), **semi-annually** (H1/H2), and **yearly**. Different values in the same schema can have different cadences, and the validator rejects silent duplicates inside an open window.

### A web console for the people who run it

The bundled admin SPA (built with Microsoft Fluent UI) lets administrators:

- Create services, issue and rotate their API keys, disable them when staff move on.
- Author schemas through a form — including the validation rules, no editor or deployment needed.
- Browse every submission, with filters by service and date range.
- Submit or edit data **on behalf of** a service (handy for back-fills, fixes, or training).
- See at a glance which services are up to date and which are behind, per KPI per period.
- Plot historical numeric data with a single click.
- Upload simple HTML+Liquid **reports** (single-submission summaries or period roll-ups) that operators can render and re-render with different filters.

Service-side users can use a slimmed-down version of the same console to file submissions through the web while they're getting started.

### …or use the API and stop typing numbers in by hand

Most KPIs already exist somewhere in the council's systems — a waste-collection schedule, a finance ledger, a roads-maintenance backlog. Re-typing those numbers into a web form every week is busywork: it eats hours, it introduces transcription errors, and it relies on someone remembering to do it before the cadence window closes.

Every action the bundled console performs — creating services, authoring schemas, submitting and editing data, checking status, exporting samples — is also available as a documented HTTP API. That means a service can drop the form entirely and let a small script do the work:

- **Schedule it.** A nightly cron job, a weekly scheduled task, or an Azure Function reads from wherever the numbers already live and `POST`s a submission. The job runs in the background; nobody has to remember anything.
- **Wire it into an existing system.** Plug the API into an internal portal, an ERP/CRM, or an integration platform (Logic Apps, Power Automate, n8n, …) so KPIs flow out as a side effect of work that's already happening.
- **Build a tailored UI.** When the bundled console doesn't fit a particular team's workflow, the same REST endpoints let you build a thin custom front-end in any language or framework.

The validator, cadence enforcement, and `/api/me/status` endpoint all stay in play — automated submissions go through exactly the same checks as manual ones, and the script can ask "what's outstanding?" before posting to avoid duplicates. The full API surface — endpoints, request/response shapes, status codes, auth — is in [docs/client/api.md](docs/client/api.md).

### A read model your dashboards already understand

Every accepted sample lands in a flat, denormalised projection that's exposed as a standard **OData v4 feed** at `/odata/samples`. Power BI consumes it directly through *Get Data → OData feed*; the same endpoint works for any OData client (Tableau, Grafana with an OData plugin, custom Excel queries, …). For tools that don't speak OData, the same data is also reachable as paged JSON.

### A security model that's easy to operate

Authentication is plain **API keys** carried in an HTTP header — no OAuth flow to set up, no token server to run. Keys are tied to accounts, can be rotated without downtime (two keys live in parallel during the handover), and are revoked individually when needed. Three roles cover the typical staff layout:

- **Service** — automated submitters; can read and write their own data only.
- **Operator** — back-office staff (data analysts, finance); can read everything but cannot delete or edit submissions.
- **Admin** — full control, including issuing keys, editing schemas, and correcting submissions retroactively.

Every change is **audited** (who did it, when) and **soft-deleted** by default — nothing important is destroyed unless an admin explicitly wipes it.

### One container, one database

The whole system ships as a single Docker image bundling the API and the admin SPA. Deploy it anywhere that runs a Linux container — the recommended target on Azure is **Container Apps + Cosmos DB for MongoDB (vCore)**, but App Service, AKS, or a self-hosted MongoDB all work. Health probes, structured logging and OpenTelemetry tracing are wired in by default. Want to see it first? `docker compose up --build` runs the whole stack locally — see the [quickstart](docs/setup/quickstart.md).

## Who it's for

| You are…                                          | …and you get                                                                                                                              | …start here                                                                                                                                  |
|---------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------|
| **Just kicking the tyres**                        | The whole thing running locally in a couple of minutes with **only Docker** — no .NET SDK, Node, or MongoDB to install.                      | [docs/setup/quickstart.md](docs/setup/quickstart.md)                                                                                         |
| A **council administrator** running the catalogue | The web console for managing services, schemas, submissions, and watching status across all services.                                       | [docs/admin-user-guide/](docs/admin-user-guide/README.md)                                                                                    |
| A **service** sending KPI data                    | A stable REST API that lets a scheduled job or an existing back-office system push KPIs automatically — no more weekly form-filling. A web form is there as a fallback (or for getting started).                                                            | [docs/client/](docs/client/README.md)                                                                                                        |
| A **data analyst / report author**                | A direct OData feed for Power BI (and equivalents), with examples and refresh-schedule guidance.                                            | [docs/setup/powerbi.md](docs/setup/powerbi.md)                                                                                               |
| A **DevOps / SRE** rolling it out                 | A step-by-step Azure deployment, an exhaustive configuration reference, and an operational checklist.                                       | [docs/setup/](docs/setup/README.md)                                                                                                          |
| A **developer / contributor**                     | Clean-architecture-ish layering, full XML doc comments, a focused test suite, and Aspire-driven local dev.                                | [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/architecture/](docs/architecture/README.md)                                                     |

## At a glance

A single .NET 10 / ASP.NET Core back-end talks to MongoDB, exposes a REST API for services, an admin REST API for the console, and an OData feed for reporting. The React admin SPA is served from the same origin in production. Everything else — telemetry, health, configuration — is standard ASP.NET Core, so it slots into whatever observability and secret-management stack you already run.

```
┌─────────────────┐                 ┌─────────────────┐               ┌────────────┐
│ Service clients │   API key       │     Ingest      │  MongoDB      │  MongoDB   │
│ (scripts, bots, │ ───────────────►│   (API + SPA,   │ ─────────────►│  (Cosmos / │
│  schedulers …)  │                 │  single image)  │  wire protocol│  hosted)   │
└─────────────────┘                 └─────────────────┘               └────────────┘
                                            ▲
                                            │ OData / REST
                                     ┌──────┴──────┐
                                     │   Power BI  │
                                     │ / dashboards│
                                     └─────────────┘
```

The deeper view — domain model, request flow, validation pipeline, auth lifecycle — lives in [docs/architecture/](docs/architecture/README.md).

## Documentation

Long-form docs live under [`docs/`](docs/README.md) and are split by audience:

- [**admin-user-guide/**](docs/admin-user-guide/README.md) — the day-to-day operator's manual.
- [**client/**](docs/client/README.md) — how a service obtains a key and uses the API.
- [**setup/**](docs/setup/README.md) — deployment, configuration, and reporting integration.
- [**architecture/**](docs/architecture/README.md) — system design and the auth model in depth.
- [CONTRIBUTING.md](CONTRIBUTING.md) — dev environment, tests, and where the source lives.

## License

[MIT](LICENSE).

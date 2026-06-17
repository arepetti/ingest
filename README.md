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

Authentication is plain **API keys** carried in an HTTP header — no OAuth flow to set up, no token server to run. Keys are tied to accounts, can be rotated without downtime (two keys live in parallel during the handover), and are revoked individually when needed. Authorisation is **capability-based**: each account holds a fine-grained set of permissions (e.g. `schemas:read`, `submissions:approve`, `accounts:manage`) that decides exactly what it can do and see. Roles are convenient templates that seed those capabilities, which you can then tune per account:

- **Service** — automated submitters; can read and write their own data only (no extra capabilities).
- **Operator** — back-office staff (data analysts, finance); seeded with read-everything capabilities, but grant a trusted one `schemas:manage` (or any other capability) without making them an admin.
- **Approver** — reviewers for the optional submission-approval workflow (view + approve submissions).
- **Admin** — every capability, non-reducible; full control including issuing keys, editing schemas, and correcting submissions retroactively.

Every change is **audited** (who did it, when) and **soft-deleted** by default — nothing important is destroyed unless an admin explicitly wipes it.

### One container, one database

The whole system ships as a single Docker image bundling the API and the admin SPA. Deploy it anywhere that runs a Linux container — the recommended target on Azure is **Container Apps + Cosmos DB for MongoDB (vCore)**, but App Service, AKS, or a self-hosted MongoDB all work. Health probes, structured logging and OpenTelemetry tracing are wired in by default. Want to see it first? `docker compose up --build` runs the whole stack locally — see the [quickstart](docs/setup/quickstart.md).

## Who it's for


| You are…                                          | …and you get                                                                                                                                                                                     | …start here                                                                              |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------- |
| **Just kicking the tyres**                        | The whole thing running locally in a couple of minutes with **only Docker** — no .NET SDK, Node, or MongoDB to install.                                                                          | [docs/setup/quickstart.md](docs/setup/quickstart.md)                                     |
| A **council administrator** running the catalogue | The web console for managing services, schemas, submissions, and watching status across all services.                                                                                            | [docs/admin-user-guide/](docs/admin-user-guide/README.md)                                |
| A **service** sending KPI data                    | A stable REST API that lets a scheduled job or an existing back-office system push KPIs automatically — no more weekly form-filling. A web form is there as a fallback (or for getting started). | [docs/client/](docs/client/README.md)                                                    |
| A **data analyst / report author**                | A direct OData feed for Power BI (and equivalents), with examples and refresh-schedule guidance.                                                                                                 | [docs/setup/powerbi.md](docs/setup/powerbi.md)                                           |
| A **DevOps / SRE** rolling it out                 | A step-by-step Azure deployment, an exhaustive configuration reference, and an operational checklist.                                                                                            | [docs/setup/](docs/setup/README.md)                                                      |
| A **developer / contributor**                     | Clean-architecture-ish layering, full XML doc comments, a focused test suite, and Aspire-driven local dev.                                                                                       | [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/architecture/](docs/architecture/README.md) |


## Documentation

Long-form docs live under `[docs/](docs/README.md)` and are split by audience:

- **[admin-user-guide/](docs/admin-user-guide/README.md)** — the day-to-day operator's manual.
- **[client/](docs/client/README.md)** — how a service obtains a key and uses the API.
- **[setup/](docs/setup/README.md)** — deployment, configuration, and reporting integration.
- **[architecture/](docs/architecture/README.md)** — system design and the auth model in depth.
- [CONTRIBUTING.md](CONTRIBUTING.md) — dev environment, tests, and where the source lives.

## Examples

Ingest is designed to be extended **without changing the product code**. The three extension points each ship ready-to-use, copy-pasteable examples for contributors and council developers who want to add a useful data page or pipeline of their own:

- **Schemas** — [`examples/schemas/*.json`](examples/schemas/) — example KPI packages (garbage collection, weekly workforce, finance month-end close). Upload one through the admin console (**Schemas → New schema → Upload JSON…**) as-is, or adapt it. See [docs/admin-user-guide/schemas.md](docs/admin-user-guide/schemas.md).
- **Reports** — [`examples/reports/html/*.html`](examples/reports/html/) — HTML + Liquid templates (single-submission summaries and period roll-ups) you upload to add a small, server-rendered data page. No editor, no redeploy. See [docs/admin-user-guide/reports.md](docs/admin-user-guide/reports.md).
- **Integrations** — [`examples/integrations/`](examples/integrations/README.md) — minimal scripts (Python, PowerShell, C#, Java) showing how a council's existing waste-collection or HR software (a CSV export, or a vendor REST API) can push KPIs to the API on a schedule.

Schemas and reports are uploaded through the admin console; integrations run wherever your scripts run. None of them require touching or rebuilding the application.

## Feature highlights

Everything Ingest does, in one place:

**Data collection & modelling**

- **Code-free schemas** — define KPI packages with per-value type, unit, reporting cadence, and required/optional flags through a form.
- **Seven cadences** — daily, weekly, fortnightly (Monday-anchored), monthly, quarterly, semi-annually, and yearly; values in the same schema can differ.
- **Per-service visibility** — services only ever see the schemas they're entitled to submit against.
- **Schema versioning** — track changes over time, with a "new value" indicator in the editor.

**Validation that runs before data lands**

- **Shape checks** — type, min/max, string length, and regex constraints.
- **Custom business rules** — a small expression language for cross-field logic, with plain yes/no or friendly custom error messages.
- **Conditional fields** — hide or disable values that don't apply in context (`Enabled if` / `Visible if`).
- **Soft warnings** — flag unusual-but-legal values without rejecting them.
- **Cadence enforcement** — at most one submission per period, per service, per KPI; silent duplicates are rejected.

**Admin web console (Fluent UI)**

- Manage services and accounts, issue/rotate/revoke API keys, disable accounts.
- Author schemas and their validation rules — no editor or redeploy.
- Browse and filter submissions; create or edit data **on behalf of** a service; bulk-import history from JSON/CSV.
- A status dashboard and **missing-submissions** analytics — who's up to date, who's behind, per KPI per period.
- One-click historical plotting and HTML + Liquid **reports** (single-submission or period roll-ups).

**Integration & reporting**

- **Full REST API** — every console action is a documented HTTP endpoint, so a cron job, Azure Function, or integration platform can submit automatically.
- **OData v4 feed** at `/odata/samples` — Power BI talks to it out of the box; also reachable as paged JSON.
- **Outbound webhooks** — signed (HMAC-SHA256), durably queued, auto-retrying HTTP pushes on `submission.accepted` / `submission.warnings` / `window.upcoming` / `window.missed`, to wire into Teams, Power Automate, or your own service without polling.
- **Email notifications** — upcoming-reminder, missed-alert, and submission-warning emails with editable Liquid templates and configurable recipients.

**Security, governance & operations**

- **API-key auth** with zero-downtime rotation and individual revocation; **capability-based authorisation** with roles (Service / Operator / Approver / Admin) as templates that seed per-account capabilities.
- **Optional SSO** (Microsoft / Google) layered on top of API keys.
- **Full audit log** and **soft-delete** by default — nothing important is destroyed silently.
- **GDPR built in** — right-to-erasure (anonymise or delete), per-subject data export, and configurable time-based retention.
- **Backup & restore** convenience tool for small deployments and environment seeding.
- **One container, one database** — health probes, structured logging, and OpenTelemetry tracing wired in; Aspire-driven local dev.

## Project status & support

Ingest is an open-source project maintained by **a single developer in their spare time**. It's offered under the [MIT licence](LICENSE) **as-is, with no warranty and no SLA** — there's no company behind it and no guaranteed response time. That's stated plainly so you can plan around it, not to put you off: the project is built to be self-supportable and you're never locked in.

- **Getting help** — best-effort via [GitHub Discussions](https://github.com/arepetti/ingest/discussions) and [Issues](https://github.com/arepetti/ingest/issues). Details and expectations: [SUPPORT.md](SUPPORT.md).
- **Security issues** — please report **privately**, never in a public issue: [SECURITY.md](SECURITY.md).
- **How the project is run** (and how to become a co-maintainer — they're welcome) — [GOVERNANCE.md](GOVERNANCE.md).
- **Relying on it in production?** Go ahead — but plan to self-support: the code is small and layered, every public type is documented, and [docs/](docs/README.md) covers deployment, configuration, and disaster recovery. The MIT licence means you can always fork and maintain your own copy.

## License

[MIT](LICENSE).
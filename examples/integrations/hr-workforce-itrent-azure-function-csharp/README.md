# Weekly workforce, from MHR iTrent, on a timer (Azure Function, C#)

A minimal **Azure Function (timer trigger)** that runs the [`hr-workforce-itrent-api-csharp`](../hr-workforce-itrent-api-csharp/) integration on a schedule **in the cloud**: it pulls a person-level personnel/absence extract from MHR iTrent's REST API, aggregates it locally into a weekly summary, and pushes one submission into Ingest against the [`weekly_workforce`](../../schemas/generic.json) schema.

## What this adds over the console example

The [console version](../hr-workforce-itrent-api-csharp/) is one-shot — you schedule it yourself with Task Scheduler, and the API key sits in a local `.cmd`. This version is the **worked cloud-scheduling example** the [integrations index](../README.md#scheduling-on-windows) points at:

- **Scheduling** is built in: a `[TimerTrigger]` with a CRON expression replaces Task Scheduler, so the job fires even when no operator's machine is on.
- **Secrets** live in **Function App settings** (or [Key Vault references](https://learn.microsoft.com/en-us/azure/app-service/app-service-key-vault-references)), not a plaintext `.cmd`.

The field mapping and aggregation are **identical** to the console example — only where it runs and how the secret is stored change.

## How it works

```
[TimerTrigger]  ->  OAuth2 token  ->  GET iTrent people/absence extract  ->  aggregate locally  ->  POST /api/submissions
```

The function reads person-level rows shaped like [`sample_response.json`](sample_response.json) and aggregates them:

| Schema value (`valueName`) | Derived from the iTrent extract                                           |
|----------------------------|---------------------------------------------------------------------------|
| `employees_active`         | count of rows with `employmentStatus = active` and `engagement = permanent` |
| `sick_leave`               | of those, how many have `sicknessThisWeek = true`                         |
| `contractors`              | count of `employmentStatus = active` with `engagement = contractor`       |
| `overtime_hours`           | sum of `overtimeHours` across active people — optional, sent only when greater than 0 |

> **Privacy note:** the iTrent extract is person-level (it even carries a `personRef`), but the function aggregates it **before anything leaves the process** and sends Ingest only the counts and totals above. No employee record — no name, no reference, no absence detail — ever reaches Ingest, which only stores KPIs. See [docs/gdpr.md](../../../docs/gdpr.md).

## The schedule

The trigger in [`WeeklyWorkforceFunction.cs`](WeeklyWorkforceFunction.cs) uses an [NCRONTAB](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-timer) expression — `{second} {minute} {hour} {day} {month} {day-of-week}`:

```csharp
[TimerTrigger("0 0 6 * * 1")]   // 06:00 every Monday, for the prior week
```

Each weekly cadence bucket accepts one submission, so weekly is the right cadence here. Adjust the expression to match your reporting window.

## Authentication (two layers)

- **To iTrent** — OAuth2 client credentials. Set `ITRENT_TOKEN_URL`, `ITRENT_CLIENT_ID` and `ITRENT_CLIENT_SECRET`; the function exchanges them for a short-lived bearer token and sends it as `Authorization: Bearer …` on the extract request. Leave these **empty** for the local sample run below (the static file needs no auth).
- **To Ingest** — one header on the POST: `X-Api-Key: {keyId}.{secret}`, issued by an Ingest administrator. See [docs/client/README.md](../../../docs/client/README.md#how-to-get-an-api-key).

All of these are read with `Environment.GetEnvironmentVariable`, so locally they come from [`local.settings.json`](local.settings.json) and in Azure from **Application settings** / Key Vault references — no code change between the two.

## Prerequisites

- .NET 10 SDK and the [Azure Functions Core Tools v4](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local) (`func`) for a local run.
- The `weekly_workforce` schema uploaded to your deployment by an admin ([`examples/schemas/generic.json`](../../schemas/generic.json)).
- An **API key** for your service account, issued by an admin.
- For a real run: iTrent OAuth2 client credentials and the extract endpoint URL, plus an Azure subscription to deploy into.

## Run it locally

### 1. Serve the sample iTrent extract (local test only)

In this folder:

```bash
python -m http.server 8000
```

This serves `sample_response.json` at `http://localhost:8000/sample_response.json` — the `SOURCE_URL` already configured in [`local.settings.json`](local.settings.json). No OAuth is needed for the local file, so the `ITRENT_*` values are left empty.

### 2. Start the function host

```bash
func start
```

The timer fires on its schedule. To trigger it immediately without waiting for Monday, use the admin endpoint the host prints on startup:

```bash
curl -X POST http://localhost:7071/admin/functions/WeeklyWorkforce -H "Content-Type: application/json" -d "{}"
```

## Deploy and configure in Azure

```bash
func azure functionapp publish <your-function-app>
```

Then set the same keys as **Application settings** (portal, `az functionapp config appsettings set`, or Key Vault references for the secrets):

```
INGEST_BASE_URL      = https://ingest.example.org
INGEST_API_KEY       = abc12345.your-secret-here
SOURCE_URL           = https://<tenant>.itrent.example/api/people/weekly-extract?unit=waste
ITRENT_TOKEN_URL     = https://<tenant>.itrent.example/oauth2/token
ITRENT_CLIENT_ID     = ingest-feed
ITRENT_CLIENT_SECRET = your-itrent-secret
```

> Keep `INGEST_API_KEY` and `ITRENT_CLIENT_SECRET` in Key Vault and reference them from app settings rather than storing the literal values.

## Expected output

In the function logs (local console or Application Insights):

```
Created submission 7c4d2e10-9a8b-4c3d-b2e1-0f9a8b7c6d5e
```

If more than 20% of the team is off sick, the schema raises a non-blocking warning logged alongside the id. On a validation failure each failing rule is logged:

```
Submission failed: HTTP 400
  error: Sick-leave count (9) cannot exceed active employees (8).
```

## See also

- [hr-workforce-itrent-api-csharp](../hr-workforce-itrent-api-csharp/) — the one-shot console version of the same logic
- [Full API reference](../../../docs/client/api.md)
- [Integrations index](../README.md)

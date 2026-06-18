# Weekly workforce, from MHR iTrent (C#)

A minimal C# integration that **pulls a person-level personnel/absence extract from MHR iTrent's REST API**, aggregates it locally into a weekly summary, and pushes one submission into Ingest against the [`weekly_workforce`](../../schemas/generic.json) schema.

## What real software this stands in for

**MHR iTrent** is a widely used HR & payroll platform. Its integration APIs are OAuth2-protected and return **person-level** personnel, absence and time data rather than ready-made aggregates. This example stands in for querying such an extract for one team (e.g. *Waste Services*) and rolling it up into KPIs on your side.

> Educated guess: the exact endpoints, scopes and field names vary by iTrent tenant and configuration. Edit the mapping in `push_workforce_itrent.cs` and the OAuth scope to match yours.

This is the only example that shows **token-based auth to the source** (OAuth2 client credentials) and **client-side aggregation**. If your HR system instead hands you a ready-aggregated summary, the [Python vendor-API example](../hr-workforce-vendor-api-python/) is simpler; if it drops a CSV, see the [PowerShell CSV example](../hr-workforce-csv-powershell/).

## How it works

```
OAuth2 token  ->  GET iTrent people/absence extract  ->  aggregate locally  ->  POST /api/submissions
```

The script reads person-level rows shaped like [`sample_response.json`](sample_response.json) and aggregates them:

| Schema value (`valueName`) | Derived from the iTrent extract                                           |
|----------------------------|---------------------------------------------------------------------------|
| `employees_active`         | count of rows with `employmentStatus = active` and `engagement = permanent` |
| `sick_leave`               | of those, how many have `sicknessThisWeek = true`                         |
| `contractors`              | count of `employmentStatus = active` with `engagement = contractor`       |
| `overtime_hours`           | sum of `overtimeHours` across active people — optional, sent only when greater than 0 |

> **Privacy note:** the iTrent extract is person-level (it even carries a `personRef`), but the script aggregates it **on your machine** and sends Ingest only the counts and totals above. No employee record — no name, no reference, no absence detail — ever reaches Ingest, which only stores KPIs. See [docs/gdpr.md](../../../docs/gdpr.md).

## Authentication (two layers)

- **To iTrent** — OAuth2 client credentials. Set `ITRENT_TOKEN_URL`, `ITRENT_CLIENT_ID` and `ITRENT_CLIENT_SECRET`; the script exchanges them for a short-lived bearer token and sends it as `Authorization: Bearer …` on the extract request. Leave these **unset** for the local sample run below (the static file needs no auth).
- **To Ingest** — one header on the POST: `X-Api-Key: {keyId}.{secret}`, issued by an Ingest administrator. See [docs/client/README.md](../../../docs/client/README.md#how-to-get-an-api-key).

## Prerequisites

- .NET 10 SDK — this is a file-based app (`dotnet run push_workforce_itrent.cs`, no `.csproj`). BCL only, no NuGet packages.
- The `weekly_workforce` schema uploaded to your deployment by an admin ([`examples/schemas/generic.json`](../../schemas/generic.json)).
- An **API key** for your service account, issued by an admin.
- For a real run: iTrent OAuth2 client credentials and the extract endpoint URL.

## Run it

### 1. Serve the sample iTrent extract (local test only)

In this folder:

```bash
python -m http.server 8000
```

This serves `sample_response.json` at `http://localhost:8000/sample_response.json` (the script's default source). No OAuth is needed for the local file, so leave the `ITRENT_*` variables unset. In production, skip this and use `--source-url` plus the `ITRENT_*` variables.

### 2. Preview the payload

```bash
dotnet run push_workforce_itrent.cs -- --dry-run
```

### 3. Submit for real

```powershell
$env:INGEST_BASE_URL = "https://ingest.example.org"
$env:INGEST_API_KEY  = "abc12345.your-secret-here"

# iTrent OAuth2 + endpoint:
$env:ITRENT_TOKEN_URL     = "https://<tenant>.itrent.example/oauth2/token"
$env:ITRENT_CLIENT_ID     = "ingest-feed"
$env:ITRENT_CLIENT_SECRET = "your-itrent-secret"

dotnet run push_workforce_itrent.cs -- --source-url "https://<tenant>.itrent.example/api/people/weekly-extract?unit=waste"
```

## Expected output

```
Created submission 7c4d2e10-9a8b-4c3d-b2e1-0f9a8b7c6d5e
```

If more than 20% of the team is off sick, the schema raises a non-blocking warning printed alongside the id. On a validation failure each failing rule is printed and the script exits non-zero:

```
Submission failed: HTTP 400
  error: Sick-leave count (9) cannot exceed active employees (8).
```

## Scheduling

Run once per week — e.g. Monday for the prior week — via cron (`0 6 * * 1`) or Windows Task Scheduler (`/SC WEEKLY /D MON`). Each weekly cadence bucket accepts one submission. See the [integrations index](../README.md) for a ready-made Windows wrapper-`.cmd` + `schtasks` recipe; keep the iTrent client secret in a secrets store rather than the wrapper for anything beyond a quick trial.

## See also

- [Full API reference](../../../docs/client/api.md)
- [Integrations index](../README.md) (incl. how to schedule it on Windows)

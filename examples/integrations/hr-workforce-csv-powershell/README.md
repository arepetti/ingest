# Weekly workforce, from an HR CSV export (PowerShell)

A minimal PowerShell integration that turns a **weekly per-employee HR/payroll export** into a workforce snapshot and pushes it into Ingest against the [`weekly_workforce`](../../schemas/generic.json) schema.

## What real software this stands in for

Many organisations run their HR and payroll on systems that can produce scheduled extracts of headcount, absence and overtime. Common products include **MHR iTrent** and **Zellis ResourceLink** (formerly Northgate), with **Civica HR**, **IRIS Cascade** and (in larger organisations) **SAP / Oracle HCM** also widely used. A weekly CSV/Excel extract is the simplest integration point and is supported by essentially all of them.

> Educated guess: product names are illustrative of the HR/payroll market for this sample domain. Column names differ per system — adjust the mapping to match your extract.

## How it works

```
workforce_export_*.csv  ->  aggregate per-employee rows  ->  POST /api/submissions
   (one row per person)        (counts + overtime total)        (one weekly submission)
```

The sample [`workforce_export_2026-06-15.csv`](workforce_export_2026-06-15.csv) has one row per employee/contractor. The script aggregates a single team's rows into the schema's weekly values:

| Schema value (`valueName`) | Derived from the CSV                                                        |
|----------------------------|-----------------------------------------------------------------------------|
| `employees_active`         | count of `status = Active` AND `employment_type = Permanent`                 |
| `sick_leave`               | count of active permanent staff with `sick_days_this_week > 0`              |
| `contractors`              | count of `status = Active` AND `employment_type = Contractor`               |
| `overtime_hours`           | sum of `overtime_hours` (optional — sent only when greater than 0)          |

Leavers and inactive rows are excluded. `overtime_hours` is optional in the schema, so it is only included when there is overtime to report.

> Privacy note: Ingest stores **aggregate KPIs**, not personal records. This script reduces per-employee rows to counts/totals before sending — no names or employee IDs leave your environment. See [docs/gdpr.md](../../../docs/gdpr.md).

## Prerequisites

- PowerShell 5.1+ (Windows) or PowerShell 7+ (cross-platform).
- The `weekly_workforce` schema uploaded to your deployment by an admin ([`examples/schemas/generic.json`](../../schemas/generic.json)).
- An **API key** for your service account, issued by an admin — see [docs/client/README.md](../../../docs/client/README.md#how-to-get-an-api-key).

## Run it

Preview the payload first:

```powershell
./Push-Workforce.ps1 -DryRun
```

Then submit for real:

```powershell
$env:INGEST_BASE_URL = "https://ingest.example.org"
$env:INGEST_API_KEY  = "abc12345.your-secret-here"
./Push-Workforce.ps1
```

Point at a different export with `-Csv path\to\your_export.csv`.

## Expected output

```
Created submission 7c4d2e10-9a8b-4c3d-b2e1-0f9a8b7c6d5e
```

If more than 20% of the team is off sick, the schema raises a non-blocking warning that is printed alongside the submission id. On a validation failure each failing rule is printed and the script exits non-zero:

```
Submission failed: HTTP 400
  error: Sick-leave count (9) cannot exceed active employees (8).
```

## Scheduling

Run once per week (e.g. Monday morning for the prior week) via Windows Task Scheduler: `powershell -File Push-Workforce.ps1`. Each weekly cadence bucket accepts one submission.

## See also

- [Full API reference](../../../docs/client/api.md)
- [Integrations index](../README.md) (incl. how to schedule it on Windows)

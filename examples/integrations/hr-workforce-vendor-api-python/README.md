# Weekly workforce, from an HR REST API (Python)

A minimal Python integration that **pulls a weekly workforce summary from an HR system's REST API** and pushes it into Ingest against the [`weekly_workforce`](../../schemas/generic.json) schema.

## What real software this stands in for

Modern HR/payroll platforms expose REST APIs alongside their file exports. Common systems include **MHR iTrent** and **Zellis ResourceLink**, with **Civica HR**, **IRIS Cascade** and **SAP / Oracle HCM** also in use. This example assumes such an API returns an already-aggregated weekly summary for a team; you point the script at it and map its fields to the schema.

> Educated guess: vendor names are illustrative of the HR/payroll market for this sample domain. Real API shapes vary — edit the mapping in `build_samples` to match.

## How it works

```
GET HR weekly-summary JSON  ->  map fields  ->  POST /api/submissions
```

The script reads a summary shaped like [`sample_response.json`](sample_response.json):

| Schema value (`valueName`) | HR API field                                            |
|----------------------------|---------------------------------------------------------|
| `employees_active`         | `headcount.permanentActive`                             |
| `sick_leave`               | `headcount.onSickLeave`                                 |
| `contractors`              | `headcount.contractorsActive`                           |
| `overtime_hours`           | `overtimeHours` — optional, sent only when greater than 0 |

> Privacy note: the API already returns **aggregates**, not personal records, and Ingest only ever stores KPIs. No employee-level data is sent. See [docs/gdpr.md](../../../docs/gdpr.md).

## Prerequisites

- Python 3.8+ (standard library only — no `pip install`).
- The `weekly_workforce` schema uploaded to your deployment by an admin ([`examples/schemas/generic.json`](../../schemas/generic.json)).
- An **API key** for your service account, issued by an admin — see [docs/client/README.md](../../../docs/client/README.md#how-to-get-an-api-key).

## Run it

### 1. Serve the sample HR response (local test only)

In this folder:

```bash
python -m http.server 8000
```

This serves `sample_response.json` at `http://localhost:8000/sample_response.json` (the script's default source). In production, skip this and use `--source-url`.

### 2. Preview the payload

```bash
python push_workforce.py --dry-run
```

### 3. Submit for real

```powershell
$env:INGEST_BASE_URL = "https://ingest.example.org"
$env:INGEST_API_KEY  = "abc12345.your-secret-here"
python push_workforce.py
# or against a real HR endpoint:
python push_workforce.py --source-url "https://hr-vendor.example/api/teams/waste/weekly-summary"
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

Run once per week via cron (`0 6 * * 1 python3 push_workforce.py`) or Windows Task Scheduler. Each weekly cadence bucket accepts one submission.

## See also

- [Full API reference](../../../docs/client/api.md)
- [Integrations index](../README.md) (incl. how to schedule it on Windows)

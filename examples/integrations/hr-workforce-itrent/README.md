# Weekly workforce, from MHR iTrent (C#, PowerShell, Python)

A minimal integration that **queries a weekly workforce summary from MHR iTrent's OData API** and pushes it into Ingest against the `[weekly_workforce](../../schemas/generic.json)` schema. The same logic is provided three times — pick whichever your team maintains:

- `**push_workforce.cs`** — C# (.NET 10 file-based app, no project file)
- `**Push-Workforce.ps1**` — Windows PowerShell 5.1+
- **`push_workforce.py`** — Python 3.8+ (standard library only)

## What real software this stands in for

[MHR iTrent](https://www.mhrglobal.com/) is a widely used HR/payroll platform. Alongside its file exports it publishes **OData feeds**, so you can ask for just the few columns you need rather than a whole extract. This example assumes a feed returns an already-aggregated weekly summary per organisation unit; you point the script at it and map its columns to the schema.

> Educated guess: the exact feed name, column names and query shape vary by iTrent configuration. Edit the field mapping in the script to match your tenant.

## How it works

```
GET iTrent OData (?$select=...&$filter=...)  ->  map columns  ->  POST /api/submissions
```

The script reads one team-week row shaped like `[sample_response.json](sample_response.json)` (an OData `value` array):


| Schema value (`valueName`) | iTrent column                                             |
| -------------------------- | --------------------------------------------------------- |
| `employees_active`         | `activeEmployees`                                         |
| `sick_leave`               | `absenceSickness`                                         |
| `contractors`              | `contingentWorkers`                                       |
| `overtime_hours`           | `overtimeHours` — optional, sent only when greater than 0 |


In production the column selection and the team/week filter ride on the URL:

```
https://itrent.example.org/odata/v1/WeeklyWorkforceSummary?$select=activeEmployees,absenceSickness,contingentWorkers,overtimeHours&$filter=organisationUnit eq 'Waste Services' and weekEnding eq 2026-06-15
```

> Privacy note: the feed returns **aggregates**, not personal records, and Ingest only ever stores KPIs. No employee-level data is sent. See [docs/gdpr.md](../../../docs/gdpr.md).

## Prerequisites

- For C#: the [.NET 10 SDK](https://dotnet.microsoft.com/). For PowerShell: Windows PowerShell 5.1+ (built in). For Python: 3.8+ (standard library only — no `pip install`). No external packages any way.
- The `weekly_workforce` schema uploaded to your deployment by an admin (`[examples/schemas/generic.json](../../schemas/generic.json)`).
- An **API key** for your service account, issued by an admin — see [docs/client/README.md](../../../docs/client/README.md#how-to-get-an-api-key).

## Run it

### 1. Serve the sample iTrent response (local test only)

In this folder:

```bash
python -m http.server 8000
```

This serves `sample_response.json` at `http://localhost:8000/sample_response.json` (the default source). In production, skip this and point at the real iTrent feed.

### 2. Preview the payload

```powershell
dotnet run push_workforce.cs -- --dry-run     # C#
./Push-Workforce.ps1 -DryRun                   # PowerShell
python push_workforce.py --dry-run             # Python
```

### 3. Submit for real

```powershell
$env:INGEST_BASE_URL = "https://ingest.example.org"
$env:INGEST_API_KEY  = "abc12345.your-secret-here"

dotnet run push_workforce.cs                    # C#
./Push-Workforce.ps1                            # PowerShell
python push_workforce.py                        # Python

# or against a real iTrent feed:
./Push-Workforce.ps1 -SourceUrl "https://itrent.example.org/odata/v1/WeeklyWorkforceSummary?$select=activeEmployees,absenceSickness,contingentWorkers,overtimeHours&$filter=organisationUnit eq 'Waste Services'"
python push_workforce.py --source-url "https://itrent.example.org/odata/v1/WeeklyWorkforceSummary?\$select=activeEmployees,absenceSickness,contingentWorkers,overtimeHours&\$filter=organisationUnit eq 'Waste Services'"
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

Run once per week (e.g. Monday for the prior week) via Windows Task Scheduler — see the [integrations index](../README.md#scheduling-on-windows). Each weekly cadence bucket accepts one submission.

## See also

- [Full API reference](../../../docs/client/api.md)
- [Integrations index](../README.md)


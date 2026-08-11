# Waste rounds, from a vendor REST API (PowerShell)

A minimal PowerShell integration that **pulls a daily collection summary from a waste-management vendor's REST API** and pushes it into Ingest against the [`garbage_collection`](../../schemas/garbage-collection.json) schema.

## What real software this stands in for

Some waste platforms expose a REST/JSON API for daily operational data rather than (or as well as) a file export. Products in this space include **Bartec Collective**, **Whitespace Work Software**, **Echo**, **Yotta Alloy** and **AMCS**. This example assumes such an endpoint returns an already-aggregated daily summary; you point the script at it and map its field names to the schema's values.

> Educated guess: vendor names are illustrative of the waste-operations market for this sample domain. Real API shapes vary — edit the mapping section to match yours.

## How it works

```
GET vendor daily-summary JSON  ->  map fields  ->  POST /api/submissions
```

The script reads a summary shaped like [`sample_response.json`](sample_response.json) and maps it as follows:

| Schema value (`valueName`)   | Vendor field                                                    |
|------------------------------|-----------------------------------------------------------------|
| `tonnes_collected`           | `summary.totalTonnage`                                          |
| `routes_completed`           | `summary.roundsCompleted`                                       |
| `routes_missed`              | `summary.roundsMissed`                                          |
| `routes_missed_reason`       | joined `missedRounds[].reason` — only when `roundsMissed > 0`   |
| `vehicle_breakdowns`         | count of `fleetIncidents` with `type = breakdown`              |
| `breakdown_description`      | joined breakdown `description`s — only when there was one       |
| `recycling_tonnes_collected` | `summary.recyclingTonnage`                                      |
| `contamination_pct`          | `summary.recyclingContaminationPercent` — only when recycling > 0 |

Conditional fields follow the schema's `visibleIf` rules, so the payload only contains a field when the server expects it.

## Prerequisites

- PowerShell 5.1+ (Windows) or PowerShell 7+ (cross-platform).
- The `garbage_collection` schema uploaded to your deployment by an admin ([`examples/schemas/garbage-collection.json`](../../schemas/garbage-collection.json)).
- An **API key** for your service account, issued by an admin — see [docs/client/README.md](../../../docs/client/README.md#how-to-get-an-api-key).

## Run it

### 1. Serve the sample vendor response (local test only)

In this folder, start a tiny static server so the script has something to GET:

```bash
python -m http.server 8000
```

That serves `sample_response.json` at `http://localhost:8000/sample_response.json` (the script's default `-SourceUrl`). In production, skip this and pass the real vendor URL with `-SourceUrl`.

### 2. Preview the payload

```powershell
./Push-WasteRounds.ps1 -DryRun
```

### 3. Submit for real

```powershell
$env:INGEST_BASE_URL = "https://ingest.example.org"
$env:INGEST_API_KEY  = "abc12345.your-secret-here"
./Push-WasteRounds.ps1
# or against a real vendor endpoint:
./Push-WasteRounds.ps1 -SourceUrl "https://waste-vendor.example/api/depots/EASTFIELD/daily-summary"
```

## Expected output

```
Created submission 3e8a1f56-1c2d-4e5f-8a9b-0c1d2e3f4a5b
  warning: Sample 'garbage_collection.contamination_pct': ...
```

A `201` with `warnings` means accepted-but-flagged. On a validation failure the script prints each failing rule and exits non-zero:

```
Submission failed: HTTP 400
  error: Routes missed (9) cannot exceed routes completed (6).
```

## Scheduling

Run once per day after collections close via Windows Task Scheduler: `powershell -File Push-WasteRounds.ps1`. Each daily cadence bucket accepts one submission.

## See also

- [Full API reference](../../../docs/client/api.md)
- [Integrations index](../README.md) (incl. how to schedule it on Windows)

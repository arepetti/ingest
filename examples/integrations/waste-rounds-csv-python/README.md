# Waste rounds, from a CSV export (Python)

A minimal Python integration that takes a **daily round-level CSV export** from a waste-management system and pushes the aggregated daily KPIs into Ingest against the [`garbage_collection`](../../schemas/garbage-collection.json) schema.

## What real software this stands in for

Waste operators (municipalities, contractors, or internal facilities teams) typically run a round-management / in-cab platform that can produce a scheduled CSV or Excel extract of the day's collections. Common products include **Bartec Collective**, **Whitespace Work Software**, **Echo** (Adur), **Yotta Alloy**, **Webaspx**, **Civica** and **AMCS**. The exact column names differ per product, so this example is deliberately vendor-agnostic: adjust the column mapping in `push_waste_rounds.py` to match your export and the rest works unchanged. A CSV drop is the lowest-common-denominator integration - almost every system can produce one even if it has no API.

> Educated guess: product names above are illustrative of the waste-operations market for this sample domain, not an endorsement or a claim about any specific deployment.

## How it works

```
rounds_export_*.csv  ->  aggregate per-round rows  ->  POST /api/submissions
   (one row per round)        (totals for the day)        (one daily submission)
```

The sample [`rounds_export_2026-06-15.csv`](rounds_export_2026-06-15.csv) has one row per collection round. The script rolls those rows up into the schema's daily values:

| Schema value (`valueName`)   | Derived from the CSV                                              |
|------------------------------|------------------------------------------------------------------|
| `tonnes_collected`           | sum of `general_waste_tonnes` + `recycling_tonnes` (gate total)  |
| `routes_completed`           | count of rows with `status = completed`                          |
| `routes_missed`              | count of rows with `status = missed`                             |
| `routes_missed_reason`       | joined `miss_reason`s — sent only when `routes_missed > 0`        |
| `vehicle_breakdowns`         | count of rows with `vehicle_breakdown = Y`                       |
| `breakdown_description`      | joined `breakdown_notes` — sent only when there was a breakdown  |
| `recycling_tonnes_collected` | sum of `recycling_tonnes`                                        |
| `contamination_pct`          | tonnage-weighted average — sent only when recycling > 0          |

The conditional fields mirror the schema's `visibleIf` rules, so the payload only includes a field when the server expects it. The monthly compliance values (`customer_complaints`, `monthly_inspection_passed`, `inspection_remediation_notes`) are out of scope for this daily job.

## Prerequisites

- Python 3.8+ (standard library only — no `pip install`).
- The `garbage_collection` schema uploaded to your Ingest deployment by an admin (use [`examples/schemas/garbage-collection.json`](../../schemas/garbage-collection.json)).
- An **API key** for your service account, issued by an Ingest administrator. You do not generate it yourself — see [docs/client/README.md](../../../docs/client/README.md#how-to-get-an-api-key). The format is `KeyId.Secret`; treat it as opaque.

## Run it

First, see exactly what would be sent without calling the API:

```bash
python push_waste_rounds.py --dry-run
```

Then submit for real (PowerShell on Windows):

```powershell
$env:INGEST_BASE_URL = "https://ingest.example.org"
$env:INGEST_API_KEY  = "abc12345.your-secret-here"
python push_waste_rounds.py
```

bash/zsh:

```bash
export INGEST_BASE_URL="https://ingest.example.org"
export INGEST_API_KEY="abc12345.your-secret-here"
python push_waste_rounds.py
```

Point at a different export with `--csv path/to/your_export.csv`.

## Expected output

On success:

```
Created submission 3e8a1f56-1c2d-4e5f-8a9b-0c1d2e3f4a5b
  warning: Sample 'garbage_collection.contamination_pct': Contamination above ...
```

A `201` with `warnings` means the data was **accepted** but flagged (e.g. unusually high tonnage). A validation failure prints the failing rules and exits non-zero:

```
Submission failed: HTTP 400
  error: Value 'garbage_collection.tonnes_collected' below min (0).
```

## Scheduling

Run it once per day after the rounds close, e.g. Windows Task Scheduler calling `python push_waste_rounds.py`, or cron: `0 18 * * * /usr/bin/python3 push_waste_rounds.py`. Each daily cadence bucket accepts one submission; re-running the same day is rejected unless you replace the existing submission (see [docs/client/api.md](../../../docs/client/api.md)).

## See also

- [Full API reference](../../../docs/client/api.md)
- [Integrations index](../README.md) (incl. how to schedule it on Windows)

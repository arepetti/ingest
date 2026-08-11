# Waste rounds, from a CSV export (C#)

A minimal C# integration that takes a **daily round-level CSV export** from a waste-management system and pushes the aggregated daily KPIs into Ingest against the [`garbage_collection`](../../schemas/garbage-collection.json) schema.

It is a **.NET 10 file-based app** — a single `.cs` file you run directly with `dotnet run`, with no `.csproj` and no build step. Only the base class library is used (no NuGet packages).

## What real software this stands in for

Waste operators (municipalities, contractors, or internal facilities teams) typically run a round-management / in-cab platform that can produce a scheduled CSV or Excel extract of the day's collections — **Bartec Collective**, **Whitespace Work Software**, **Echo**, **Yotta Alloy**, **Webaspx**, **Civica**, **AMCS**, and others. Column names differ per product, so the mapping in `push_waste_rounds.cs` is the part you adapt; the rest is generic.

> Educated guess: product names are illustrative of the waste-operations market for this sample domain, not an endorsement.

## How it works

```
rounds_export_*.csv  ->  aggregate per-round rows  ->  POST /api/submissions
   (one row per round)        (totals for the day)        (one daily submission)
```

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

The request JSON is assembled by hand (file-based apps disable reflection-based serialization by default), which also makes the exact payload shape explicit.

## Prerequisites

- **.NET 10 SDK** (`dotnet --version` reports `10.x`). File-based `dotnet run` needs .NET 10+.
- The `garbage_collection` schema uploaded to your deployment by an admin ([`examples/schemas/garbage-collection.json`](../../schemas/garbage-collection.json)).
- An **API key** for your service account, issued by an admin — see [docs/client/README.md](../../../docs/client/README.md#how-to-get-an-api-key).

## Run it

Preview the payload without calling the API:

```powershell
dotnet run push_waste_rounds.cs -- --dry-run
```

Then submit for real:

```powershell
$env:INGEST_BASE_URL = "https://ingest.example.org"
$env:INGEST_API_KEY  = "abc12345.your-secret-here"
dotnet run push_waste_rounds.cs
```

Everything after `--` is passed to the program; point at a different export with `-- --csv path\to\your_export.csv`.

> First run compiles the file and may take a few seconds; later runs are cached. For a faster scheduled job you can publish an executable (`dotnet publish push_waste_rounds.cs -o out`) and run that instead.

## Expected output

```
Created submission 3e8a1f56-1c2d-4e5f-8a9b-0c1d2e3f4a5b
  warning: Sample 'garbage_collection.contamination_pct': ...
```

A `201` with `warnings` means accepted-but-flagged. On a validation failure each failing rule is printed and the program exits non-zero:

```
Submission failed: HTTP 400
  error: Value 'garbage_collection.tonnes_collected' below min (0).
```

## See also

- [Full API reference](../../../docs/client/api.md)
- [Integrations index](../README.md) (incl. how to schedule it on Windows)

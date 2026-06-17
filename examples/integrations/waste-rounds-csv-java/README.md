# Waste rounds, from a CSV export (Java)

A minimal Java integration that takes a **daily round-level CSV export** from a waste-management system and pushes the aggregated daily KPIs into Ingest against the [`garbage_collection`](../../schemas/garbage-collection.json) schema.

It is a **single-file program** — run it directly with `java PushWasteRounds.java` (Java 11+ source-file mode), no build tool and no dependencies. It uses only the JDK (`java.net.http` for HTTP, `java.nio.file` for the CSV).

## What real software this stands in for

Councils (or their waste contractors) typically run a round-management / in-cab platform that can produce a scheduled CSV or Excel extract of the day's collections — **Bartec Collective**, **Whitespace Work Software**, **Echo**, **Yotta Alloy**, **Webaspx**, **Civica**, **AMCS**, and others. Column names differ per product, so the mapping in `PushWasteRounds.java` is the part you adapt; the rest is generic.

> Educated guess: product names are illustrative of the local-government waste market, not an endorsement.

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

The request JSON is assembled by hand to avoid pulling in a JSON library; the raw response body (containing the new submission id and any warnings) is printed as-is.

## Prerequisites

- **JDK 11 or newer** (`java -version`). Source-file `java Foo.java` execution needs Java 11+.
- The `garbage_collection` schema uploaded to your deployment by an admin ([`examples/schemas/garbage-collection.json`](../../schemas/garbage-collection.json)).
- An **API key** for your service account, issued by an admin — see [docs/client/README.md](../../../docs/client/README.md#how-to-get-an-api-key).

## Run it

Preview the payload without calling the API:

```powershell
java PushWasteRounds.java --dry-run
```

Then submit for real:

```powershell
$env:INGEST_BASE_URL = "https://ingest.example.org"
$env:INGEST_API_KEY  = "abc12345.your-secret-here"
java PushWasteRounds.java
```

Point at a different export with `--csv path\to\your_export.csv`.

> The example runs from source for simplicity. For a packaged scheduled job, compile it first with `javac PushWasteRounds.java` and run `java PushWasteRounds`.

## Expected output

```
Created submission (HTTP 201): {"id":"3e8a1f56-...","warnings":["..."]}
```

A `201` means accepted; a non-empty `warnings` array means accepted-but-flagged. On a validation failure the status and response body are printed and the program exits non-zero:

```
Submission failed: HTTP 400
  {"title":"Validation failed","status":400,"errors":["..."]}
```

## See also

- [Full API reference](../../../docs/client/api.md)
- [Integrations index](../README.md) (incl. how to schedule it on Windows)

# Integration examples

End-to-end, copy-pasteable examples of pushing data from a typical local-council source system into Ingest. Each folder is one self-contained integration: a script, its sample source data, and a README.

These are **educated-guess illustrations** of how a council's existing software (waste-collection / HR) would feed Ingest. The vendor names mentioned are representative of the local-government market, not endorsements; the field mappings are the part worth copying.

## The examples

| Example | Domain | Source style | Language | Schema |
|---------|--------|--------------|----------|--------|
| [waste-rounds-csv-python](waste-rounds-csv-python/) | Garbage collection | CSV export | Python | `garbage_collection` |
| [waste-rounds-csv-csharp](waste-rounds-csv-csharp/) | Garbage collection | CSV export | C# (.NET 10) | `garbage_collection` |
| [waste-rounds-csv-java](waste-rounds-csv-java/) | Garbage collection | CSV export | Java (11+) | `garbage_collection` |
| [waste-rounds-vendor-api-powershell](waste-rounds-vendor-api-powershell/) | Garbage collection | Vendor REST API | PowerShell | `garbage_collection` |
| [hr-workforce-csv-powershell](hr-workforce-csv-powershell/) | HR / workforce | CSV export | PowerShell | `weekly_workforce` |
| [hr-workforce-vendor-api-python](hr-workforce-vendor-api-python/) | HR / workforce | Vendor REST API | Python | `weekly_workforce` |
| [hr-workforce-itrent](hr-workforce-itrent/) | HR / workforce | MHR iTrent OData API | C# (.NET 10), PowerShell, Python | `weekly_workforce` |
| [hr-workforce-itrent-api-csharp](hr-workforce-itrent-api-csharp/) | HR / workforce | MHR iTrent REST API (OAuth2) | C# (.NET 10) | `weekly_workforce` |
| [hr-workforce-itrent-azure-function-csharp](hr-workforce-itrent-azure-function-csharp/) | HR / workforce | MHR iTrent REST API + Azure Function timer | C# (.NET isolated) | `weekly_workforce` |

The spread is intentional: two domains, two source styles (a scheduled **CSV/Excel export** vs a **REST API**), and several languages (Python, PowerShell, C#, Java). Whatever your real system looks like, one of these is close enough to adapt — the four waste **CSV** examples are the same logic in four languages, so pick whichever your team is comfortable maintaining.

## Two source styles

- **CSV export** — the lowest common denominator. Almost every system can drop a scheduled CSV/Excel extract on a share or SFTP; the script reads it, aggregates, and submits. Vendor-agnostic by design — just remap the columns.
- **Vendor REST API** — for systems that expose an HTTP endpoint of already-shaped data. The script GETs it, maps fields, and submits. The samples ship a static JSON file you can serve locally with `python -m http.server` to run end-to-end.

## What every example has in common

1. **Auth** — one header on the POST: `X-Api-Key: {keyId}.{secret}`. No OAuth, no cookies. The key is issued by an Ingest administrator; you never generate it yourself. See [docs/client/README.md](../../docs/client/README.md#how-to-get-an-api-key).
2. **Config via environment variables**:
   - `INGEST_BASE_URL` — e.g. `https://ingest.example.org`
   - `INGEST_API_KEY` — your service account's key
3. **A dry-run switch** that prints the exact payload without calling the API — run this first.
4. **One endpoint**: `POST /api/submissions` with a body of `{ "samples": [ { schemaName, valueName, value, timestamp, note } ] }`.
5. **Schema-aware mapping** — all samples in one POST share a single `schemaName`, values are typed, conditional fields are sent only when their `visibleIf` condition holds, and one submission covers one cadence bucket (daily/weekly).
6. **Result handling** — print the new submission `id`, surface any non-blocking `warnings`, and on a `400` print the failing rules from the `errors[]` array.

## Before you run anything

1. An administrator uploads the relevant schema ([`examples/schemas/garbage-collection.json`](../schemas/garbage-collection.json) and/or [`examples/schemas/generic.json`](../schemas/generic.json)) and issues your service an API key.
2. Set `INGEST_BASE_URL` and `INGEST_API_KEY`.
3. Run the example in preview mode to inspect the payload, then run it for real. The dry-run switch is `--dry-run` (Python, Java), `-DryRun` (PowerShell), or `-- --dry-run` (the C# file-based app — everything after `--` goes to the program).

## Scheduling on Windows

These scripts are one-shot: they push the current period's data and exit, so you run them on a schedule (daily for waste, weekly for HR). On Windows the simplest option is **Task Scheduler**, driven from the command line with `schtasks`.

> This is a deliberately **naive example**: the schedule runs on an operator's own computer, so it only fires when that machine is on, awake, and signed in, and the API key sits in a local file. It's fine for a quick trial or a small team. A more robust setup runs the job in the cloud — an **Azure Function** (timer trigger), **Power Automate**, an **Azure Logic App**, a CI/CD scheduled pipeline, or a cron job on an always-on server — with the key held in a secrets store (Key Vault, the platform's secret manager) rather than a `.cmd`. The mapping logic is identical; only where it runs and how the secret is stored change. For a worked version of the Azure Function approach, see [hr-workforce-itrent-azure-function-csharp](hr-workforce-itrent-azure-function-csharp/) — the same iTrent logic on a weekly `[TimerTrigger]`, with secrets in Function App settings / Key Vault.

A reliable pattern is a tiny wrapper `.cmd` that sets the secrets and invokes the script, then a scheduled task that runs the wrapper. Create `run-waste.cmd` next to the example (keep it out of source control — it holds your key):

```bat
@echo off
set INGEST_BASE_URL=https://ingest.example.org
set INGEST_API_KEY=abc12345.your-secret-here
cd /d "%~dp0"
python push_waste_rounds.py
```

Register it to run every day at 18:00:

```bat
schtasks /Create /TN "Ingest waste rounds" /TR "C:\path\to\run-waste.cmd" /SC DAILY /ST 18:00 /RL LIMITED /F
```

Useful follow-ups:

```bat
schtasks /Run    /TN "Ingest waste rounds"      &rem run it now to test
schtasks /Query  /TN "Ingest waste rounds" /V /FO LIST   &rem last run time + result
schtasks /Delete /TN "Ingest waste rounds" /F   &rem remove it
```

Notes:

- A scheduled task uses no console, so prefer the wrapper for environment variables rather than relying on an interactive session's `set`/`$env:`.
- For a weekly HR job use `/SC WEEKLY /D MON` (e.g. Monday for the prior week).
- `/RL LIMITED` runs without elevation; use `/RU`/`/RP` to run under a specific service account. Consider Windows Credential Manager or a secrets store instead of a plaintext key in the `.cmd` for anything beyond a quick trial.
- The same idea works for any example — swap the last line of the wrapper for `powershell -File Push-WasteRounds.ps1`, `java PushWasteRounds.java`, or `dotnet run push_waste_rounds.cs`.

## See also

- [docs/client/README.md](../../docs/client/README.md) — getting started for service clients
- [docs/client/api.md](../../docs/client/api.md) — full service-facing API reference
- [examples index](../README.md)

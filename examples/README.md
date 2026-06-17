# Examples

Ready-to-use, copy-pasteable examples for extending Ingest **without changing the product code**. Each category below is an extension point: schemas and reports are uploaded through the admin console, integrations run wherever you schedule them.

## Categories

### Schemas

KPI packages an administrator uploads (**Schemas → New schema → Upload JSON…**) to define what services may submit. See [schemas/](schemas/README.md).

| File | Schema name | What it covers |
|------|-------------|----------------|
| [garbage-collection.json](schemas/garbage-collection.json) | `garbage_collection` | Daily kerbside-collection operations (tonnage, routes, fleet, recycling). |
| [generic.json](schemas/generic.json) | `weekly_workforce` | A lightweight weekly headcount/availability snapshot. |
| [finance-monthly-close.json](schemas/finance-monthly-close.json) | `finance_monthly_close` | A monthly finance close with budget/variance and reconciliation checks. |

### Reports

HTML + Liquid templates an administrator uploads (**Reports → Upload report**) to add a small, server-rendered data page. The templates live in [reports/html/](reports/html/); see [reports/](reports/README.md).

### Integrations

Scripts that collect data from a source system and submit it to Ingest — the "how would my council's existing software feed this?" examples. See [integrations/](integrations/README.md) for the shared conventions (auth, environment variables, dry-run, error handling) and how to **schedule a script on Windows**.

| Example | Domain | Source style | Language |
|---------|--------|--------------|----------|
| [waste-rounds-csv-python](integrations/waste-rounds-csv-python/) | Garbage collection | CSV export | Python |
| [waste-rounds-csv-csharp](integrations/waste-rounds-csv-csharp/) | Garbage collection | CSV export | C# (.NET 10) |
| [waste-rounds-csv-java](integrations/waste-rounds-csv-java/) | Garbage collection | CSV export | Java (11+) |
| [waste-rounds-vendor-api-powershell](integrations/waste-rounds-vendor-api-powershell/) | Garbage collection | Vendor REST API | PowerShell |
| [hr-workforce-csv-powershell](integrations/hr-workforce-csv-powershell/) | HR / workforce | CSV export | PowerShell |
| [hr-workforce-vendor-api-python](integrations/hr-workforce-vendor-api-python/) | HR / workforce | Vendor REST API | Python |

### Power BI

Ready-made starting points for exploring the data in Power BI over the `/odata/samples` feed — the [recommended way to explore Ingest data](../docs/setup/powerbi.md). See [powerbi/](powerbi/README.md).

| Example | What it is |
|---------|-----------|
| [ingest-samples](powerbi/ingest-samples/) | A full PBIP (text-format project) you open in Power BI Desktop; three pages across all example schemas. |
| [waste-quickstart](powerbi/waste-quickstart/) | A docs-only, copy-paste `.m`/`.dax` mini-example for the `garbage_collection` schema. |

## See also

- [docs/client/](../docs/client/) — service-client documentation (auth, full API reference)
- [docs/admin-user-guide/schemas.md](../docs/admin-user-guide/schemas.md) — authoring/uploading schemas
- [docs/admin-user-guide/reports.md](../docs/admin-user-guide/reports.md) — authoring/uploading reports

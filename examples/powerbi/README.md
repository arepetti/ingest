# Power BI examples

Ready-made starting points for exploring Ingest data in **Power BI**, pointed at the OData feed at `/odata/samples`. This is the [recommended, primary way to explore Ingest data](../../docs/setup/powerbi/README.md) — the admin SPA dashboard and built-in reports are deliberately basic; real slicing, trends and charting belong in a BI tool.

Both artifacts here use the exact connection recipe from [docs/setup/powerbi/](../../docs/setup/powerbi/README.md): an OData feed with **Anonymous** auth plus an `X-Api-Key` custom header, with the key held in a Power Query **parameter** rather than baked into the file.

## The two artifacts

| Folder | What it is | Reach for it when… |
|--------|-----------|--------------------|
| [ingest-samples/](ingest-samples/) | A full **PBIP** (text-format Power BI project) you open in Power BI Desktop. Three report pages, one per [example schema](../schemas/README.md), with a flattened value column and a calendar table for time-intelligence. | You want a working report to open, point at your deployment, and build on. |
| [waste-quickstart/](waste-quickstart/) | A **docs-only** mini-example: a README plus copy-paste `.m` and `.dax` snippets for the `garbage_collection` schema only. No project files. | You'd rather paste a couple of queries into a blank report and assemble it yourself in five minutes — or the full PBIP won't open and you want the raw pieces. |

The full example covers all three schemas; the quickstart is intentionally just waste, as the smallest end-to-end illustration.

## Required role (both)

The feed is gated by the **Operator** policy: any account with role `Operator` or `Admin` can read it — a `Service`-role key cannot. Issue a dedicated Operator-kind credential for the report so revoking it later doesn't affect anybody else. See the [admin guide](../../docs/admin-user-guide/accounts.md) for how to create one.

## Before you start

1. An admin uploads the [example schemas](../schemas/README.md) and there are some submissions to look at (run an [integration example](../integrations/README.md) to generate data, or use the dashboard).
2. You have an **Operator** API key and your deployment's base URL (e.g. `https://ingest.example.org`).

## See also

- [docs/setup/powerbi/](../../docs/setup/powerbi/README.md) — the full connection guide: column references, header recipe, query options, data-model tips, scheduled refresh.
- [docs/setup/excel.md](../../docs/setup/excel.md) — the same feed in Excel, the cheapest analyst on-ramp.
- [examples index](../README.md)

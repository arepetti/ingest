# Example reports

Ready-made report templates you can upload to an Ingest deployment **without changing the product code**. A report is a server-rendered HTML page (with a small YAML front-matter block and a [Liquid](https://shopify.github.io/liquid/) body) that renders against either a single submission or an aggregated period of submissions for a schema. An administrator uploads one via **Reports → Upload report** in the admin console; operators then view and re-render it with different filters.

Reports are a deliberately simple, developer-oriented feature for small canned summaries — **not** an analytics tool. For real exploration, slicing and charting, point Power BI (or any OData client) at the `/odata/samples` feed instead. See [docs/setup/powerbi.md](../../docs/setup/powerbi.md).

## Templates

The HTML templates live in [`html/`](html/):

| File | Type | Targets | What it does |
|------|------|---------|--------------|
| [single_submission_table.html](html/single_submission_table.html) | Single | global | Renders any submission as a plain table. Useful for ad-hoc review. |
| [garbage_collection_daily_summary.html](html/garbage_collection_daily_summary.html) | Single | `garbage_collection` | One-page summary card with headline KPIs and a per-value table. |
| [workforce_weekly_aggregate.html](html/workforce_weekly_aggregate.html) | Aggregate | `weekly_workforce` | Min/avg/max/sum/count per cadence bucket, across services. |
| [multi_schema_aggregate.html](html/multi_schema_aggregate.html) | Aggregate | `garbage_collection`, `finance_monthly_close`, `weekly_workforce` | Flat per-service samples list — the viewer picks the schema. |

All four use inline CSS so they render fine in the viewer's sandboxed iframe with no external assets. They target the [example schemas](../schemas/README.md) — upload those first.

## Use one

1. Sign in as an **Admin** and open **Reports**.
2. Click **Upload report** and pick a `.html` file from [`html/`](html/).
3. Open the report and use the filter bar to pick a schema/period (and a submission for `Single` reports), then **Render**.

## See also

- [docs/admin-user-guide/reports.md](../../docs/admin-user-guide/reports.md) — authoring reference: front matter, the data envelope, filters, uploading.
- [examples index](../README.md)

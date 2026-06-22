# Waste quickstart — build it yourself in five minutes

A no-project, copy-paste path to a Power BI report over the `garbage_collection` schema. No `.pbix` or `.pbip` to download or repair — just three snippet files you paste into a blank report:

| File | Where it goes |
|------|---------------|
| [samples.m](samples.m) | The query source (Advanced Editor) — pulls the feed, pre-filtered to waste. |
| [value.m](value.m) | A custom column that flattens the typed columns into one `Value`. |
| [measures.dax](measures.dax) | Starter measures (tonnes, recycling rate, contamination, missed routes…). |

Prefer a ready-made model across all three schemas? Use the full [ingest-samples](../ingest-samples/) project instead.

## Prerequisites

- Power BI Desktop (or Excel — the M and the connection work there too; see [docs/setup/excel.md](../../../docs/setup/excel.md)).
- An **Operator** (or Admin) API key and your deployment's base URL. A `Service`-role key cannot read the feed.
- The `garbage_collection` schema uploaded and some submissions in it (run the [waste integration example](../../integrations/README.md) to generate data).

## Steps

1. **New parameters.** In Power BI Desktop: **Home > Transform data** to open Power Query, then **Manage Parameters > New Parameter**. Add two, both **Type = Text**:
   - `BaseUrl` — e.g. `https://ingest.example.org` (no trailing slash).
   - `ApiKey` — your key, form `keyId.secret`.
2. **New source query.** **Home > New Source > Blank Query**, open the **Advanced Editor**, and paste [samples.m](samples.m). Rename the query to `Samples`. Click **Done**.
3. **Anonymous auth.** If prompted for credentials, pick **Anonymous** — the key rides along as the `X-Api-Key` header from the query, not the dialog. ([Why?](../../../docs/setup/powerbi/README.md#why-anonymous--custom-header))
4. **Flatten the value.** With `Samples` selected: **Add Column > Custom Column**, name it `Value`, and paste the body of [value.m](value.m). Leave the original `*Value` columns in place — the measures use them.
5. **Close & Apply.**
6. **Add the measures.** For each block in [measures.dax](measures.dax): **Modeling > New measure** and paste it.
7. **Chart it.** A good first page:
   - Line chart — Axis `PeriodStart` (or `Timestamp`), Values `[Total tonnes collected]` and `[Recycling tonnes]`.
   - Gauge — `[Avg contamination %]`.
   - Cards — `[Routes missed]`, `[Recycling rate %]`.
   - Slicer — `ServiceName`.

> For proper time-intelligence (YTD, same-period-last-year), add a calendar table and relate it to `Timestamp` — see [docs/setup/powerbi/samples.md § Suggested data model](../../../docs/setup/powerbi/samples.md#suggested-data-model). The full [ingest-samples](../ingest-samples/) project already includes one.

## Pre-filtering more

[samples.m](samples.m) already filters to `SchemaName eq 'garbage_collection'`. Narrow further by editing the URL in the source step, e.g. one service and the last 12 months:

```m
BaseUrl & "/odata/samples?$filter=SchemaName eq 'garbage_collection' and ServiceName eq 'roads-team' and Timestamp ge 2025-06-01T00:00:00Z"
```

See [docs/setup/powerbi/samples.md § Pre-filtering at the source](../../../docs/setup/powerbi/samples.md#pre-filtering-at-the-source) for the full set of options.

## See also

- [docs/setup/powerbi/](../../../docs/setup/powerbi/README.md) — the authoritative guide (column references, data model, refresh).
- [ingest-samples](../ingest-samples/) — the full PBIP across all three schemas.
- [Power BI examples index](../README.md)

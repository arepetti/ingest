# Samples feed (the data)

The `samples` feed is the **primary data source** for Power BI — the raw, denormalised history of every value reading. Reach for it for trends, comparisons, snapshots and any custom analysis.

```
/odata/samples
```

> **First time here?** Authentication, the custom-header recipe, the `ApiKey` parameter, query options and scheduled refresh are all covered once on the **[hub page](README.md)**. This page is just what's specific to the samples feed.

## What you get

The feed serves rows from the `SampleProjection` collection: **one row per sample** (one value reading), not per submission. Each row is fully denormalised, so Power BI gets readable, self-contained data with no joins to perform.

### Columns

| Column            | Type                  | Notes |
|-------------------|-----------------------|-------|
| `Id`              | `Edm.Guid`            | Projection row id. |
| `SubmissionId`    | `Edm.Guid`            | The parent submission. Use it to roll up samples that arrived together. |
| `ServiceAccountId`| `Edm.Guid`            | Owning account id. |
| `ServiceName`     | `Edm.String`          | Machine-style account name (e.g. `roads-team`). |
| `SchemaName`      | `Edm.String`          | Schema name. |
| `ValueName`       | `Edm.String`          | Value name inside the schema. |
| `ValueType`       | `Edm.String`          | One of `String`, `Integer`, `Number`, `Date`, `Boolean`. Tells you which `*Value` column is populated. |
| `StringValue`     | `Edm.String?`         | Populated when `ValueType = String`. |
| `IntegerValue`    | `Edm.Int64?`          | Populated when `ValueType = Integer`. |
| `NumberValue`     | `Edm.Double?`         | Populated when `ValueType = Number`. |
| `DateValue`       | `Edm.DateTimeOffset?` | Populated when `ValueType = Date`. |
| `BooleanValue`    | `Edm.Boolean?`        | Populated when `ValueType = Boolean`. |
| `Timestamp`       | `Edm.DateTimeOffset`  | When the sample was **measured**. |
| `SubmittedAt`     | `Edm.DateTimeOffset`  | When the parent submission was **reported** (accepted by the API). Use it to tell late/back-dated entries from on-time ones. |
| `Note`            | `Edm.String?`         | Free-form note attached to the sample. |
| `Cadence`         | `Edm.String`          | `Daily` / `Weekly` / `Fortnightly` / `Monthly` / `Quarterly` / `SemiAnnually` / `Yearly`. |
| `PeriodStart`     | `Edm.DateTimeOffset`  | Inclusive start of the cadence bucket the sample falls in. |
| `PeriodEnd`       | `Edm.DateTimeOffset`  | Exclusive end of that bucket. |
| `CreatedAt` / `CreatedBy` / `ModifiedAt` / `ModifiedBy` / `IsDeleted` | … | Standard audit fields. Soft-deleted rows are excluded server-side. |

**`Timestamp` vs `SubmittedAt`** — `Timestamp` is *when it happened* (the measurement), `SubmittedAt` is *when it was reported*. They differ for back-filled history and late entries; use `Timestamp` for trend axes and `SubmittedAt` to audit reporting punctuality.

> **Legacy rows and `SubmittedAt`.** The projection is rebuilt on every submission save, so `SubmittedAt` is set for everything saved since the field was introduced. Rows from older submissions not re-saved since carry the default `0001-01-01`; re-saving or replacing the submission backfills it (a brand-new submission's accept time is always stamped). The handful of submissions old enough to predate the field on the parent submission itself keep the default until re-created.

## Pre-filtering at the source

For large deployments it pays to ask the server for less data. The OData [query options](README.md#query-options) are all honoured — a few common recipes:

| Want | URL |
|------|-----|
| Last 12 months only | `…/odata/samples?$filter=Timestamp ge 2025-06-01T00:00:00Z` |
| One service | `…/odata/samples?$filter=ServiceName eq 'roads-team'` |
| One schema | `…/odata/samples?$filter=SchemaName eq 'monthly_kpis'` |
| Only one value | `…/odata/samples?$filter=SchemaName eq 'monthly_kpis' and ValueName eq 'tonnes'` |
| Exclude empty numbers | `…/odata/samples?$filter=NumberValue ne null` |
| Newest first | `…/odata/samples?$orderby=Timestamp desc` |
| Field selection | `…/odata/samples?$select=ServiceName,SchemaName,ValueName,NumberValue,Timestamp` |

Combine freely with `and` / `or`. In the Power Query source step it's cleanest to build the URL from your `BaseUrl` parameter:

```m
Source = OData.Feed(
    BaseUrl & "/odata/samples?$filter=SchemaName eq 'monthly_kpis' and Timestamp ge 2025-01-01T00:00:00Z",
    null,
    [ Implementation = "2.0", Headers = [ #"X-Api-Key" = ApiKey ] ]
)
```

## Suggested data model

Power BI handles the long (one-row-per-sample) format well as-is, but for cleaner charts you'll usually want to:

1. **Collapse the typed columns into a single `Value`.** In Power Query:

   ```m
   #"Added Value" = Table.AddColumn(#"Renamed Columns", "Value", each
       if [ValueType] = "Number"  then Number.From([NumberValue])  else
       if [ValueType] = "Integer" then Number.From([IntegerValue]) else
       if [ValueType] = "Date"    then DateTime.From([DateValue])  else
       if [ValueType] = "Boolean" then Logical.From([BooleanValue]) else
       [StringValue])
   ```

   For numeric KPIs specifically, a `NumericValue = COALESCE(NumberValue, IntegerValue)` column lets Number and Integer values aggregate together.

2. **Hide the individual `*Value` columns** from the report view to keep the field list tidy.
3. **Add a calendar table** and relate it to `Timestamp` for time-intelligence (`DATESYTD`, `SAMEPERIODLASTYEAR`, …).
4. **Use `Cadence` + `PeriodStart`** for trend visuals that already align to the cadence buckets the validator uses — no extra bucketing needed.

The [`ingest-samples` example project](../../../examples/powerbi/ingest-samples/) ships all of this pre-built (the `Value` column, a `NumericValue` column, a calendar table and a handful of starter measures).

## Troubleshooting

For connection/auth/refresh issues common to every feed, see the [hub troubleshooting](README.md#troubleshooting). Feed-specific gotchas:

### Some columns show as `null` for entire rows

Expected. The projection has a separate column per type so Power BI gets correct typing — only the column matching the row's `ValueType` is populated. Use the [collapse-to-`Value` step](#suggested-data-model) to flatten.

### My report needs joins to schema metadata

The flat projection deliberately denormalises `SchemaName` / `ValueName` / `ServiceName`, but schema-level metadata — *label*, *unit*, *required*, target bands — lives in `/api/admin/schemas`, not in the feed. Until the planned [`/odata/schemas`](README.md#odata-endpoints) feed lands, pull that endpoint as a **second query** (Get Data → Web, same `X-Api-Key` header) and join on the names in Power Query. (For target bands specifically, the [scorecard feed](scorecard.md) already carries the band edges per cell.)

### A value I expect is missing

Soft-deleted samples are excluded server-side, and so are submissions that aren't live yet (e.g. awaiting approval). Check the submission's state in the admin UI.

## Alternative: the admin query endpoint

If OData isn't an option, `POST /api/admin/query` returns the **same projection** as a paged JSON document. Request body:

```json
{
  "serviceIds":     ["…", "…"],
  "schemaNames":    ["monthly_kpis"],
  "from":           "2026-01-01T00:00:00Z",
  "to":             "2026-12-31T23:59:59Z",
  "latestOnly":     false,
  "includeDeleted": false,
  "page":           1,
  "pageSize":       500,
  "sort":           "timestamp"
}
```

Same role requirement (`query:read` — Operator or higher) and same `X-Api-Key` header. The response is a standard `PagedResponse<SampleProjectionDto>` with the same columns as the feed (including `SubmittedAt`).

Use this from custom dashboards (Grafana plugins, n8n flows, Excel via Power Query Web) when an OData connector isn't available.

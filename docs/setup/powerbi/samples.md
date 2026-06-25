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

## Incremental refresh (large / multi-year history)

A full refresh re-reads the whole feed every time — fine for a small deployment, slow once you have a few years of history (the feed [pages at 500 rows](README.md#server-side-limits--paging), so a big unfiltered reload is many sequential requests). **Power BI [incremental refresh](https://learn.microsoft.com/power-bi/connect-data/incremental-refresh-overview)** fixes that: it loads old data once, then on each refresh only re-queries a recent window. The samples feed is built for this — but **partition on the right column**, or you'll get silently stale dashboards.

### Partition on `SubmittedAt`, not `Timestamp`

Incremental refresh slices the table into date partitions and, after the first load, only ever re-reads partitions inside the *refresh window*; everything older is archived and never re-queried until a full refresh. That only works if a row's partition key **never changes**. Here's the difference:

| Column | Stable per row? | Use it for |
|--------|-----------------|------------|
| `Timestamp` | **No** — it's the measurement time, freely back-dated by submitters and editable. A back-dated or corrected sample would jump partitions (or land in an already-archived one). | The **report axis** (the relationship to your calendar table). |
| `SubmittedAt` | **Yes** — stamped once when the submission is first accepted and never moved on edit, replace, or approval. | The **partition key** for incremental refresh. |

So you partition by *reporting time* (`SubmittedAt`) while your visuals still trend by *measurement time* (`Timestamp`). Report consumers never see the difference — partitioning is invisible to them.

### Set it up

1. **Create the two reserved parameters.** Incremental refresh requires parameters named exactly `RangeStart` and `RangeEnd`, both of type **Date/Time**. **Home → Manage Parameters → New** for each (any default values; Power BI overrides them per partition).
2. **Filter the source on `SubmittedAt` between them**, so the filter folds to an OData `$filter ... ge / lt` and each partition is one small server request:

   ```m
   Source = OData.Feed(
       BaseUrl & "/odata/samples",
       null,
       [ Implementation = "2.0", Headers = [ #"X-Api-Key" = ApiKey ] ]
   ),
   Filtered = Table.SelectRows(Source, each
       [SubmittedAt] >= DateTimeZone.From(RangeStart) and
       [SubmittedAt] <  DateTimeZone.From(RangeEnd))
   ```

   `SubmittedAt` is `Edm.DateTimeOffset`, so wrap the (Date/Time) parameters in `DateTimeZone.From` to keep the comparison — and therefore the fold — clean. Keep your flatten/`Value` steps *after* this filter.
3. **Define the policy.** Right-click the `Samples` table → **Incremental refresh** → toggle it on and choose, for example, *Archive data starting **3 years** before refresh date* and *Incrementally refresh data in the last **2 months***. Pick the archive span to cover the history you report on and the refresh window to comfortably exceed how late a correction ever arrives (see the caveat below).
4. **(Optional) Detect data changes → `ModifiedAt`.** With this ticked, Power BI skips re-loading an in-window partition unless its `ModifiedAt` moved — `ModifiedAt` is refreshed whenever a submission's projection is rebuilt, so an edit bumps it. *Caveat:* the feed folds `$filter` / `$select` / `$orderby` / `$count` but **not** `$apply` aggregation, so the per-partition `max(ModifiedAt)` probe can't be pushed to the server and Power BI computes it by reading that partition's rows. Inside a tight refresh window that's bounded and harmless, but it means the big win here is **archiving the old partitions**, not the change-detection probe. Leave it off and a tight window if you prefer simplicity.
5. **Verify it folds.** In Power Query, **Tools → Query Diagnostics** (or right-click the `Filtered` step → *View Native Query* isn't available for OData, so use diagnostics) and confirm you see one `$filter=SubmittedAt ge … and SubmittedAt lt …` request per partition rather than one giant pull.

### The caveat you must plan around

Because partitions outside the refresh window are never re-read, three things **won't reach an already-published dataset** until the next *full* refresh, when they touch data whose `SubmittedAt` is older than the window:

- an **admin editing/correcting an old submission**,
- a **bulk history import back-filled with old dates** — [imported submissions are dated to their first sample's timestamp](../../admin-user-guide/submissions.md#bulk-importing-historical-submissions-requires-submissionssubmit), so they land in old partitions, not today's,
- a **soft-deleted** old submission (its rows linger in the archived partition).

Normal *late reporting* is fine: a January figure submitted today gets `SubmittedAt = today`, lands in the current (in-window) partition, and is picked up. The problem is only retroactive changes to *old* `SubmittedAt`. Mitigations:

- **Size the refresh window** to exceed your realistic correction lag (a council that fixes prior-month numbers should refresh the last *two* months, not two weeks).
- **Run a one-off full refresh after a bulk history import** (or do the import *before* first publishing the dataset).
- **Schedule an occasional full refresh** (e.g. monthly) as a backstop so any stray old edit eventually lands.

If retroactive edits to deep history are routine for you, incremental refresh may cost more vigilance than it's worth — stay on full refresh, or keep the archive span short.

## Troubleshooting

For connection/auth/refresh issues common to every feed, see the [hub troubleshooting](README.md#troubleshooting). Feed-specific gotchas:

### Some columns show as `null` for entire rows

Expected. The projection has a separate column per type so Power BI gets correct typing — only the column matching the row's `ValueType` is populated. Use the [collapse-to-`Value` step](#suggested-data-model) to flatten.

### My report needs joins to schema metadata

The flat projection deliberately denormalises `SchemaName` / `ValueName` / `ServiceName`, but schema-level metadata — *label*, *unit*, *required*, target bands — isn't in this feed. Pull it from the dedicated [**schemas feed**](schemas.md) as a second OData query (same `X-Api-Key` header) and relate it on `SchemaName` + `ValueName` — see [schemas.md → Power BI source](schemas.md#power-bi-source) for the expand-and-join recipe. (For target bands specifically, the [scorecard feed](scorecard.md) also carries the band edges per cell.)

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

# Scorecard feed (RAG status board)

The `scorecard` feed is a **pre-computed Red/Amber/Green status board** — one flat row per **(schema, value, service)** cell. It's the same board the admin [Explore page](../../admin-user-guide/explore.md) shows on its Scorecard tab, shaped for Power BI.

```
/odata/scorecard(mode='LatestAvailable',period='Current')
```

Reach for it when leadership wants an at-a-glance "who is green / amber / red **against target**" view and you don't want to re-derive the bands in DAX. For trends, history or anything numeric over time, use the [samples feed](samples.md) instead.

Only numeric values that carry a **target band** (an amber and/or green range) are included; values with no band, non-numeric values, and disabled schemas/values are omitted entirely. (Configure bands per value on the schema editor — see the [Explore page docs](../../admin-user-guide/explore.md).)

> **First time here?** Authentication, the custom-header recipe, the `ApiKey` parameter, query options and scheduled refresh are all on the **[hub page](README.md)**. This page covers only the scorecard function.

## It's a function, not a plain feed

Unlike `samples`, the scorecard is computed on demand, so it's exposed as an OData **unbound function** that takes two parameters. You call it by putting the arguments in the URL:

```
/odata/scorecard(mode='LastPeriod',period='LatestClosed')
```

| Parameter | Values | Meaning |
|-----------|--------|---------|
| `mode`    | `LatestAvailable` *(default)* | Each service's **most recent** sample for the value, however old. Services that never reported the value are omitted. Best for "where does everyone stand right now". |
|           | `LastPeriod` | Exactly **one period** (see `period`). **Every** service the schema applies to gets a row; one that didn't report that period comes back as a `Missing` row. Best for "who reported on time, and how did they do". |
| `period`  | `Current` *(default)* | The period containing "now" (still open). Only used when `mode='LastPeriod'`. |
|           | `LatestClosed` | The most recent fully-elapsed period (the one before the current). Only used when `mode='LastPeriod'`. |

Both arguments are **required by the call syntax** — pass the defaults explicitly if you don't care (`scorecard(mode='LatestAvailable',period='Current')`). Values are **case-insensitive**, and an unrecognised value falls back to the default rather than erroring.

> **Period basis (`LastPeriod` only).** `Current` answers "for the period we're in, who's reported?" — useful mid-period to chase stragglers. `LatestClosed` answers "for the last finished period, how did everyone do?" — useful for a stable monthly/quarterly review that won't shift as new data lands.

## Columns

| Column         | Type                  | Notes |
|----------------|-----------------------|-------|
| `Id`           | `Edm.String`          | Stable synthetic key `schema|value|service|periodStart` (good for incremental refresh / de-dup). |
| `SchemaName`   | `Edm.String`          | Schema name. |
| `SchemaLabel`  | `Edm.String?`         | Friendly schema label. |
| `ValueName`    | `Edm.String`          | Value name inside the schema. |
| `ValueLabel`   | `Edm.String?`         | Friendly value label. |
| `Unit`         | `Edm.String?`         | Unit of measure. |
| `Cadence`      | `Edm.String`          | `Daily` … `Yearly`. |
| `ServiceId`    | `Edm.Guid`            | Owning account id. |
| `ServiceName`  | `Edm.String`          | Machine-style account name. |
| `ServiceLabel` | `Edm.String?`         | Friendly service label. |
| `PeriodStart`  | `Edm.DateTimeOffset`  | Inclusive start of the period the cell belongs to (or was expected for). |
| `PeriodEnd`    | `Edm.DateTimeOffset`  | Exclusive end of that period. |
| `Value`        | `Edm.Double?`         | The reported number; `null` on a `Missing` row. |
| `Status`       | `Edm.String`          | `Green`, `Amber`, `Red`, or `Missing`. **Always populated** — slice and conditionally-format on it directly. |
| `SubmissionId` | `Edm.Guid?`           | Source submission; `null` on a `Missing` row. |
| `SubmittedAt`  | `Edm.DateTimeOffset?` | When that submission was accepted; `null` on a `Missing` row. (Legacy un-rebuilt rows show `0001-01-01` rather than `null` — same caveat as [samples](samples.md#what-you-get).) |
| `AmberMin` / `GreenMin` / `GreenMax` / `AmberMax` | `Edm.Double?` | Target band edges, so you can plot the targets without re-deriving them. Any edge may be `null`. |

**`Status` values:**

- `Green` — inside the ideal range.
- `Amber` — inside the acceptable range but outside the ideal range.
- `Red` — outside the acceptable range.
- `Missing` — `mode='LastPeriod'` only: the service didn't report this value for the chosen period. `Value`, `SubmissionId` and `SubmittedAt` are all `null`.

The four band edges define the ranges: red is outside `[AmberMin, AmberMax]`, green is inside `[GreenMin, GreenMax]`, amber is the rest. Any edge can be `null` (an open-ended band).

`$filter`, `$select`, `$orderby`, `$top`/`$skip` and `$count` all work on top of the result, with the same page-size 500 / max-`$top` 5000 limits and the same `query:read` auth as every other feed.

## Power BI source

Point a second OData query at the function URL, reusing the [header recipe](README.md#connecting-power-bi-desktop) and your `ApiKey` / `BaseUrl` parameters:

```m
Source = OData.Feed(
    BaseUrl & "/odata/scorecard(mode='LastPeriod',period='LatestClosed')",
    null,
    [ Implementation = "2.0", Headers = [ #"X-Api-Key" = ApiKey ] ]
)
```

Useful slices:

| Want | URL |
|------|-----|
| Only off-target cells | `…/odata/scorecard(mode='LatestAvailable',period='Current')?$filter=Status ne 'Green'` |
| Only the unreported ones | `…/odata/scorecard(mode='LastPeriod',period='LatestClosed')?$filter=Status eq 'Missing'` |
| One schema | `…?$filter=SchemaName eq 'monthly_kpis'` |
| Lean columns | `…?$select=SchemaLabel,ValueLabel,ServiceLabel,Status,Value` |

### Building the visual

A natural layout is a **matrix**: `ServiceLabel` on rows, `ValueLabel` (or `SchemaLabel` → `ValueLabel`) on columns, `Value` in the cells, with **conditional formatting → Background color → Field value** driven by a measure that maps `Status` to a colour:

```dax
StatusColor =
SWITCH(
    SELECTEDVALUE(Scorecard[Status]),
    "Green",   "#1a7f37",
    "Amber",   "#bf8700",
    "Red",     "#cf222e",
    "Missing", "#9ca3af",
    "#ffffff"
)
```

Because `Status` is already text (never blank), no DAX classification against the band edges is needed — though the `AmberMin`/`GreenMin`/`GreenMax`/`AmberMax` columns are there if you want to draw target lines on a companion chart.

## Notes

> **Scope by service in the client.** Unlike the admin endpoint, the function has **no service filter** parameter — pull the whole board and filter with `$filter` or a Power BI slicer on `ServiceLabel`.

> **Call the function, not the entity set.** Browsing `/odata` lists a `scorecardCards` set, but it has no standalone reader (the data is computed) — a plain `GET /odata/scorecardCards` returns 404. Always invoke `scorecard(mode='…',period='…')`.

> **Two queries, one model.** It's common to load **both** feeds into the same report — `samples` for trends and drill-down, `scorecard` for the status overview — relating them on `ServiceName`/`SchemaName`/`ValueName` (or `ServiceId`).

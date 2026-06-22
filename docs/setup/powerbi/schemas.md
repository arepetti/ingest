# Schemas feed (metadata catalogue)

The `schemas` feed is a **simplified, read-only catalogue** of your schema definitions — names, labels, units, types, cadences and the charting band edges. Its job is to **label and bucket** the raw rows from the [samples feed](samples.md): load it as a second query and join on names, instead of hand-rolling a `/api/admin/schemas` JSON join.

```
/odata/schemas
```

> **First time here?** Authentication, the custom-header recipe, the `ApiKey` parameter, query options and scheduled refresh are all on the **[hub page](README.md)**. This page covers only what's specific to the schemas feed.

It is deliberately **thin**. The operational surface of a schema — versioning, notes, layout, approval policy, validation expressions and the restricted-audience list — is **not** exposed; for that, use the admin API (`/api/admin/schemas`) or the admin UI. What you get is exactly the metadata a BI model needs.

## Required role

Unlike the other feeds (which gate on `query:read`), the schemas feed requires the **`schemas:read`** capability — it's schema metadata, not reporting data. In practice an **Operator** or **Admin** key carries both `query:read` *and* `schemas:read`, so the same dedicated Operator credential you use for [samples](samples.md) also reads this feed. (If you use [custom capabilities](../../admin-user-guide/accounts.md), the exact gate is `schemas:read`.)

## What you get

**One row per schema**, with the schema's values nested as a child collection. Only **live** (non-deleted) schemas are returned; the whole catalogue is small, so it loads in a single page.

### Schema columns

| Column        | Type           | Notes |
|---------------|----------------|-------|
| `Name`        | `Edm.String`   | Machine-style schema name. The key — filter on it (`Name eq 'monthly_kpis'`). Join `samples`.`SchemaName` to this. |
| `Label`       | `Edm.String?`  | Friendly schema label. |
| `Description` | `Edm.String?`  | Free-form description. |
| `Enabled`     | `Edm.Boolean`  | `false` when the schema is disabled (rejects submissions). |
| `IsGlobal`    | `Edm.Boolean`  | `true` when every service may submit; `false` means audience-restricted. |
| `Values`      | collection     | The value definitions (see below), nested inline. Expand it in Power Query. |

### Value columns (inside `Values`)

| Column     | Type          | Notes |
|------------|---------------|-------|
| `Name`        | `Edm.String`  | Machine-style value name. Join `samples`.`ValueName` to this (together with the schema name). |
| `Label`       | `Edm.String?` | Friendly value label — the natural display name for charts. |
| `Description` | `Edm.String?` | Free-form value description. |
| `Type`        | `Edm.String`  | One of `String`, `Integer`, `Number`, `Date`, `Boolean`. |
| `Unit`        | `Edm.String?` | Unit of measure (e.g. `t`, `hours`, `%`). |
| `Cadence`     | `Edm.String`  | `Daily` / `Weekly` / `Fortnightly` / `Monthly` / `Quarterly` / `SemiAnnually` / `Yearly`. |
| `Required`    | `Edm.Boolean` | Whether a sample is required when the submission is created. |
| `Enabled`     | `Edm.Boolean` | `false` when the value is disabled — it still appears here but is rejected on submission and never shows up in the scorecard/recent samples. |
| `Min` / `Max` | `Edm.Double?` | Inclusive numeric bounds (Integer/Number); `null` when unset. |
| `AmberMin` / `GreenMin` / `GreenMax` / `AmberMax` | `Edm.Double?` | Red/Amber/Green target-band edges, so you can draw target lines without re-deriving them. Any edge may be `null`. These are the same edges the [scorecard feed](scorecard.md) carries per cell. |

> **Property names are PascalCase** — `Name`, `SchemaName`, `Cadence` — exactly as written, like every other feed (see the [hub query-options note](README.md#query-options)).

## Pre-filtering at the source

OData filtering is the input mechanism — there are no function parameters. Filter on `Name`:

| Want | URL |
|------|-----|
| One schema | `…/odata/schemas?$filter=Name eq 'monthly_kpis'` |
| A few named schemas | `…/odata/schemas?$filter=Name in ('monthly_kpis','safety')` |
| Only enabled schemas | `…/odata/schemas?$filter=Enabled eq true` |
| Lean columns | `…/odata/schemas?$select=Name,Label` |

The standard page-size 500 / max-`$top` 5000 limits apply, but a real deployment has only a handful of schemas, so a single request returns everything.

## Power BI source

Point a second OData query at the feed, reusing the [header recipe](README.md#connecting-power-bi-desktop) and your `ApiKey` / `BaseUrl` parameters:

```m
Source = OData.Feed(
    BaseUrl & "/odata/schemas",
    null,
    [ Implementation = "2.0", Headers = [ #"X-Api-Key" = ApiKey ] ]
)
```

The `Values` column comes through as a nested table. To get a flat value-metadata table you can relate to `samples`, **expand** it in Power Query:

```m
#"Expanded Values" = Table.ExpandTableColumn(
    Source, "Values",
    {"Name", "Label", "Type", "Unit", "Cadence", "Required", "Enabled", "Min", "Max", "AmberMin", "GreenMin", "GreenMax", "AmberMax"},
    {"ValueName", "ValueLabel", "ValueType", "Unit", "Cadence", "Required", "Enabled", "Min", "Max", "AmberMin", "GreenMin", "GreenMax", "AmberMax"}
)
```

That yields one row per (schema, value). Relate it to the [samples](samples.md) table on `SchemaName` + `ValueName` (and rename the schema-level `Name` to `SchemaName` first) so your visuals can show `ValueLabel`/`Unit` instead of the raw machine names — and draw target lines from the band edges.

> **Replaces the manual join workaround.** Before this feed existed, the only way to get value labels/units into a report was to pull `/api/admin/schemas` as a Web (JSON) query and flatten it by hand. This feed is the supported replacement — same `X-Api-Key` header, native OData typing, no JSON wrangling.

## See also

- **[samples.md](samples.md)** — the raw data feed this catalogue labels; see its [data-model tips](samples.md#suggested-data-model).
- **[scorecard.md](scorecard.md)** — the pre-computed RAG board (already carries band edges per cell).
- **[hub page](README.md)** — auth, query options, refresh and troubleshooting shared by every feed.

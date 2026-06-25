# Power BI template (.pbit) — a reusable, schema-agnostic starter

A **distributable** starting point: assemble it once, **save it as a `.pbit` template**, and hand that single file to your analysts. On open, a `.pbit` prompts for the `BaseUrl` and `ApiKey` parameters and builds the report — no Power Query surgery, no `.pbix` to repair, nothing baked in.

Unlike the other two examples this one is **schema-agnostic** (it doesn't filter to a single schema) and ships **canonical, org-wide measures** — reporting punctuality, freshness, time-intelligence — so every report built from it agrees on what "% on time" means.

| Example | What it is | Reach for it when… |
|---------|-----------|--------------------|
| **template** (this folder) | Copy-paste pieces you assemble once, then **export as a `.pbit`** to distribute. Schema-agnostic + canonical measures. | You want one file to hand round the org, with shared definitions. |
| [waste-quickstart](../waste-quickstart/) | Docs-only snippets, waste schema only. | The fastest single-schema look. |
| [ingest-samples](../ingest-samples/) | A full **PBIP** (text-format project) across the three example schemas. | You want a ready report to open and extend in source control. |

> **Why source pieces and not a binary `.pbit` in the repo?** A `.pbit` is a binary archive; a hand-authored one risks being subtly corrupt, and a meaningful template depends on *your* deployment's schemas anyway. So this folder ships the text pieces and the 10-minute assembly + export below. You produce the `.pbit` once and distribute *that*.

## What you get

- `samples.m` — the **Samples** table source (schema-agnostic), with the typed columns already flattened into `Value` and a numeric-only `NumericValue`.
- `schemas.m` — an optional **Schemas** dimension (labels, units, types, RAG band edges) to join on for friendly labels.
- `calendar.m` — a **Calendar** table for time-intelligence, auto-ranged from your data.
- `measures.dax` — the canonical measures (see below).

## Required role

An **Operator** (or Admin) API key — a `Service`-role key cannot read the feeds. Issue a **dedicated** Operator credential for the report so revoking it later affects nobody else (see the [accounts guide](../../../docs/admin-user-guide/accounts.md)).

> **One file, per-department data.** If you give that credential a [service scope](../../../docs/admin-user-guide/accounts.md#service-scope-limiting-an-operator-to-a-subset-of-services), the feeds it reads are confined to its services *server-side*. So you can publish the **same** `.pbit`-derived report once per directorate, each dataset carrying a department-scoped key, and each audience only ever sees its own data. See [docs/setup/powerbi/](../../../docs/setup/powerbi/README.md#required-role).

## Assemble it (once)

1. **New parameters.** Power BI Desktop > **Home > Transform data** to open Power Query > **Manage Parameters > New**. Add two, **Type = Text**:
   - `BaseUrl` — e.g. `https://ingest.example.org` (no trailing slash).
   - `ApiKey` — your Operator key, form `keyId.secret`.
2. **Samples query.** **Home > New Source > Blank Query** > **Advanced Editor**, paste [samples.m](samples.m), rename to `Samples`, **Done**. Choose **Anonymous** if prompted ([why?](../../../docs/setup/powerbi/README.md#why-anonymous--custom-header)).
3. **Calendar query.** Another Blank Query, paste [calendar.m](calendar.m), rename to `Calendar`.
4. *(Optional)* **Schemas query.** Another Blank Query, paste [schemas.m](schemas.m), rename to `Schemas`.
5. **Close & Apply.**
6. **Relationships** (Model view):
   - `Calendar[Date]` → `Samples[Timestamp]` (one-to-many, single direction). Then **Table tools > Mark as date table** on `Calendar`.
   - *(If you added Schemas)* add a key column `Key = [SchemaName] & "|" & [ValueName]` to `Samples` (Add Column > Custom Column), and relate `Schemas[Key]` → `Samples[Key]`.
7. **Measures.** For each block in [measures.dax](measures.dax): **Modeling > New measure**, paste.
8. **A first page.** Slicers on `SchemaName`, `ValueName`, `ServiceName`; a line chart of `[Value (avg)]` by `Calendar[Date]`; cards for `[On-time %]`, `[Days since last reading]`, `[Reporting services]`.

## Save and distribute as a `.pbit`

1. **File > Export > Power BI template**, write a short description (it shows on open), save `Ingest.pbit`.
2. Hand that one file to analysts. On open it **prompts for `BaseUrl` and `ApiKey`** and loads — nothing else to wire.

> The key is a parameter, so it's **never baked into the `.pbit`** — each person supplies their own (ideally a department-scoped one). On the Power BI Service, set parameter values per dataset (**Dataset settings > Parameters**). See [docs/setup/powerbi/README.md § Refresh schedule](../../../docs/setup/powerbi/README.md#refresh-schedule).

## The canonical measures

Defined once here so every report agrees:

| Measure | Answers |
|---------|---------|
| `Value (sum)` / `Value (avg)` / `Latest value` | The KPI itself, in current filter context. |
| `Submissions` / `On-time submissions` / `On-time %` / `Late submissions` | **Reporting punctuality** — was it reported (`SubmittedAt`) before its cadence window closed (`PeriodEnd`)? |
| `Days since last reading` | **Freshness** — how stale is this KPI? |
| `Reporting services` | How many services are in scope. |
| `Value (avg) YTD` / `… vs last year` / `… rolling 3 months` | Time-intelligence off the Calendar table. |

They aggregate whatever is in filter context, so drop `SchemaName` / `ValueName` on a slicer or matrix. Rename and specialise per report — the waste example shows value-specific measures like `[Recycling rate %]`.

## See also

- [docs/setup/powerbi/](../../../docs/setup/powerbi/README.md) — the authoritative connection guide (columns, header recipe, refresh, scoping).
- [docs/setup/powerbi/schemas.md](../../../docs/setup/powerbi/schemas.md) — the schema-metadata feed and the expand-and-join recipe.
- [ingest-samples](../ingest-samples/) — the full PBIP if you'd rather start from a built project.
- [Power BI examples index](../README.md).

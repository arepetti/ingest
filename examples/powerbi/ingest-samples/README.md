# Ingest samples — full Power BI project (PBIP)

A ready-to-open **Power BI project** wired to the Ingest OData feed at `/odata/samples`, with a data model that covers all three [example schemas](../../schemas/README.md): `garbage_collection`, `weekly_workforce`, and `finance_monthly_close`.

It's stored in the **PBIP** (Power BI Project) format — plain text (TMDL + JSON) instead of a binary `.pbix` — so it lives in git as readable, diffable files. You open `ingest-samples.pbip` in Power BI Desktop.

## What's in the box

```
ingest-samples/
├─ ingest-samples.pbip                  ← open this in Power BI Desktop
├─ ingest-samples.SemanticModel/        ← the data model (tables, parameters, measures)
│  └─ definition/
│     ├─ expressions.tmdl               ← BaseUrl + ApiKey parameters
│     └─ tables/Samples.tmdl, Calendar.tmdl
└─ ingest-samples.Report/               ← three pages: Waste, Workforce, Finance
```

The model:

- **`Samples`** — the OData feed, with the doc's [flatten step](../../../docs/setup/powerbi.md#suggested-data-model) adding a single `Value` column, plus a `NumericValue` calculated column (`COALESCE(NumberValue, IntegerValue)`) so Number and Integer KPIs aggregate together, and a `Date` column for the relationship. A handful of starter **measures** are defined (total tonnes, recycling rate, avg contamination, routes missed, avg active employees, budget actual, revenue collected).
- **`Calendar`** — a DAX calendar (`CALENDAR(2024-01-01, 2027-12-31)`) related to `Samples[Date]`, for time-intelligence and clean month/quarter axes.

## First-time setup (required)

Because the key must not live in the file, the connection is driven by two **parameters** you set after opening:

1. Open `ingest-samples.pbip` in **Power BI Desktop** (a recent version — PBIP is GA but the option may need enabling under *File > Options > Preview features > Power BI Project (.pbip) save format* on older builds).
2. Go to **Home > Transform data > Edit parameters** (or **Manage parameters**) and set:
   - **`BaseUrl`** — your deployment, e.g. `https://ingest.example.org` (no trailing slash).
   - **`ApiKey`** — an **Operator** (or Admin) API key in the form `keyId.secret`. A `Service`-role key will get a 401.
3. When prompted for credentials on the OData source, choose **Anonymous** — the key travels as the `X-Api-Key` header set by the query, not through the credential dialog. (See [why anonymous + header](../../../docs/setup/powerbi.md#why-anonymous--custom-header).)
4. **Close & Apply.** The model refreshes against your data.

> Don't commit your key. Leave `ApiKey` as the placeholder in `expressions.tmdl`; set the real value locally, and at workspace level when publishing to the Power BI service.

## Building the visuals

The three pages (**Waste**, **Workforce**, **Finance**) open with a title and a hint textbox but **no charts yet** — this project is hand-authored text, so the visuals are left for you to drop in (it takes a minute and avoids shipping a brittle hand-written visual layout). Suggested starters, all sliced by a `SchemaName` + `ServiceName` slicer and a `Calendar[Date]` range slicer:

**Waste**
- Line chart — Axis `Calendar[Month]`, Values `[Total tonnes collected]` and `[Recycling tonnes]`.
- Gauge — Value `[Avg contamination %]`.
- Cards — `[Routes missed]`, `[Recycling rate %]`.

**Workforce**
- Line/column chart — Axis `Calendar[Month]`, Values `NumericValue` (Sum), Legend `ValueName`, filtered to `employees_active`, `sick_leave`, `contractors`.
- Card — `[Avg active employees]`.

**Finance**
- Clustered column — Axis `Calendar[Month]`, Values `[Budget actual (GBP)]` and `budget_planned` (Sum of `NumericValue` filtered to that value).
- Clustered column — `[Revenue collected (GBP)]` vs `revenue_target`.
- KPI — `invoices_paid_on_time_pct`.

## Refresh & publish

After publishing to the Power BI service, set **Data source credentials > Anonymous** and configure a **Refresh schedule** (Daily is plenty). Behind a private network, add an on-premises data gateway. Full detail: [docs/setup/powerbi.md § Refresh schedule](../../../docs/setup/powerbi.md#refresh-schedule).

## If the project won't open

This PBIP is authored by hand and **hasn't been round-tripped through Power BI Desktop** in this repo, so Desktop may want to repair or re-serialise a file on first open. If it refuses to load:

- Use the [waste-quickstart](../waste-quickstart/) snippets (or the `Samples` query and measures here) to rebuild the model in a blank report in a few minutes — the M, the flatten step, and the DAX are the parts worth copying.
- Then **Save as** PBIP to regenerate a Desktop-blessed version.

## See also

- [docs/setup/powerbi.md](../../../docs/setup/powerbi.md) — the authoritative connection guide (column reference, pre-filtering, data model, refresh).
- [waste-quickstart](../waste-quickstart/) — the no-project, copy-paste version (waste only).
- [Power BI examples index](../README.md)

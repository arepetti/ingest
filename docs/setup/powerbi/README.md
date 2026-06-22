# Power BI integration

> **This is the recommended, primary way to explore Ingest data.** The admin SPA's dashboard and the built-in reports are deliberately basic (a health check and a developer-authored convenience respectively). For real analysis — filtering, slicing, trends, charts, large datasets — connect a BI tool to the OData feeds described here.

Ingest publishes its data as **OData v4 feeds** under `/odata`. Any OData v4 client can consume them (Excel, Tableau, custom code); this guide focuses on **Microsoft Power BI Desktop**, the most common case. The same connection recipe applies to [Excel](../excel.md).

> **In a hurry?** [`examples/powerbi/`](../../../examples/powerbi/README.md) has two ready-made starters that apply everything below: a full text-format project ([`ingest-samples`](../../../examples/powerbi/ingest-samples/)) and a copy-paste `.m`/`.dax` quickstart ([`waste-quickstart`](../../../examples/powerbi/waste-quickstart/)).

## OData endpoints

There are three feeds today. Pick by what you want to do — most reports use **samples**; reach for **scorecard** when you only need the at-a-glance status, and **schemas** for the value labels/units/bands to dress up the others.

| Feed | URL | One row per | Reach for it when… | Reference |
|------|-----|-------------|--------------------|-----------|
| **Samples** (raw data) | `/odata/samples` | sample (one value reading) | you want trends, history, comparisons, or any custom analysis | **[samples.md](samples.md)** |
| **Scorecard** (RAG board) | `/odata/scorecard(mode='…',period='…')` | schema × value × service cell | you want an at-a-glance "who is green / amber / red" against targets, without re-deriving the bands | **[scorecard.md](scorecard.md)** |
| **Schemas** (metadata) | `/odata/schemas` | schema (values nested) | you want labels, units, types, cadences or band edges to join onto the samples rows | **[schemas.md](schemas.md)** |

Everything that is **common to all feeds** — authentication, the custom-header recipe, query options, the API-key parameter, scheduled refresh and connection troubleshooting — is documented **once on this page**. The per-feed pages cover only what's specific to them (columns, parameters, worked examples).

## Required role

The **samples** and **scorecard** feeds are gated by the **`query:read`** capability; the **schemas** metadata feed is gated by **`schemas:read`**. In practice both gates are satisfied by an account whose role is **Operator** or **Admin** (they carry `query:read` *and* `schemas:read` by default), so one dedicated Operator credential reads every feed; a **Service**-role key cannot read any of them. If you use [custom capabilities](../../admin-user-guide/accounts.md), the exact gates are the `query:read` / `schemas:read` capabilities rather than the role name.

Issue a **dedicated Operator-kind credential** for each report or workspace — that way revoking or rotating it later doesn't affect anybody else. See the [accounts guide](../../admin-user-guide/accounts.md) for how to create one and copy its `X-Api-Key`.

## Connecting Power BI Desktop

The OData connector in Power BI doesn't have a field for an API key, so we connect **Anonymously** and attach the key as a custom HTTP header inside the Power Query script. This is a one-time setup per query.

1. **Get Data → OData feed.**
2. Switch to **Advanced** and enter the feed URL under **URL parts**, e.g. `https://ingest.example.org/odata/samples`. Leave the query options box empty unless you want to [pre-filter](samples.md#pre-filtering-at-the-source).
3. On the credentials dialog choose **Anonymous**. *(The key goes in as a header next — not here.)*
4. Click **Connect**, then **Transform Data** to open the Power Query editor.
5. Find the source step — it looks like `= OData.Feed("https://…/odata/samples", null, [Implementation="2.0"])` — and add a `Headers` option carrying your key:

   ```m
   Source = OData.Feed(
       "https://ingest.example.org/odata/samples",
       null,
       [
           Implementation = "2.0",
           Headers = [ #"X-Api-Key" = ApiKey ]
       ]
   )
   ```

6. **Close & Apply.** Power BI loads the data and you can build visuals.

### Store the key in a parameter (don't hard-code it)

In step 5 above, `ApiKey` is a **[Power Query parameter](https://learn.microsoft.com/power-query/power-query-query-parameters)**, not a literal — so the key never gets baked into a shared `.pbix`. To create it: **Home → Manage Parameters → New**, name it `ApiKey`, type *Text*, and paste your Operator key as the current value. A `BaseUrl` parameter (e.g. `https://ingest.example.org`) is handy too, so you can repoint every query at staging/production in one place:

```m
Source = OData.Feed(
    BaseUrl & "/odata/samples",
    null,
    [ Implementation = "2.0", Headers = [ #"X-Api-Key" = ApiKey ] ]
)
```

When you publish to the Power BI service, set the parameter values at the dataset/workspace level (Dataset settings → Parameters), or supply the key through a personal/enterprise gateway.

### Why "Anonymous + custom header"?

Power BI's OData connector only offers Anonymous, Windows, and a couple of Excel-style web-key options — none of which send an arbitrary `X-Api-Key` header. Attaching the header through `OData.Feed`'s `Headers` option is the supported workaround, and it keeps working on **scheduled refresh** in the Power BI service as long as the key lives in a parameter (or a gateway). Excel uses the [exact same trick](../excel.md#why-anonymous--custom-header).

## Query options

All feeds honour the standard OData v4 system query options. Pushing work to the server keeps refreshes fast on large deployments.

| Option | Purpose | Example |
|--------|---------|---------|
| `$filter` | Restrict rows | `?$filter=SchemaName eq 'monthly_kpis'` |
| `$select` | Restrict columns | `?$select=ServiceName,SchemaName,NumberValue,Timestamp` |
| `$orderby` | Sort | `?$orderby=Timestamp desc` |
| `$top` / `$skip` | Take / page | `?$top=100&$skip=200` |
| `$count` | Include a total count | `?$count=true` |

> **Property names are PascalCase** — they match the column names in each feed's reference table exactly (`SchemaName`, `Timestamp`, `Status`, …). Use them as written.

Useful `$filter` operators and functions:

| Need | Snippet |
|------|---------|
| Equality / inequality | `Status eq 'Red'`, `ServiceName ne 'roads-team'` |
| Comparison (dates, numbers) | `Timestamp ge 2025-06-01T00:00:00Z`, `NumberValue gt 100` |
| Combine | `... and ...`, `... or ...`, `not (...)` |
| Date window (half-open) | `Timestamp ge 2026-01-01T00:00:00Z and Timestamp lt 2027-01-01T00:00:00Z` |
| Text | `startswith(ServiceName,'roads')`, `contains(SchemaName,'kpi')` |
| Null checks | `Note ne null` |

Notes:

- **Date/time literals** are bare ISO-8601 in UTC (`2026-01-01T00:00:00Z`) — *no* quotes. String literals use **single quotes** (`'roads-team'`); double a literal quote to escape it (`'O''Brien'`).
- Power BI also folds filters you apply in Power Query down to the server, but putting an explicit `$filter` in the source URL guarantees a smaller payload from request 1.

### Server-side limits & paging

The feeds page at **500 rows** and cap `$top` at **5000** per request. Power BI handles paging transparently — a large unfiltered refresh just issues many sequential page requests. If you rate-limit by IP, account for this (see [performance.md → Data volume](../performance.md#data-volume) and [hosting.md](../hosting.md)). The single biggest lever on refresh time is [pre-filtering at the source](samples.md#pre-filtering-at-the-source).

## Refresh schedule

After publishing to the Power BI service:

1. In **Dataset settings → Data source credentials**, set the authentication method to **Anonymous**. (The key is supplied by the Power Query `Headers` option, not the credential dialog.)
2. Set a **Refresh schedule** that fits your data cadence; even Daily is usually fine because each row is tiny.
3. Behind a private network, install an **on-premises data gateway** and add the OData source to it.
4. Keep the `ApiKey` (and `BaseUrl`) **parameter values** current in the dataset settings — that's where you update them after a key rotation.

## Troubleshooting

**"Unable to connect" / 401.**
Almost always the header isn't being sent. Open the source step in Power Query — your `OData.Feed` call must include the `Headers = [ #"X-Api-Key" = ApiKey ]` option. A plain `OData.Feed("…")` carries no credentials. Also confirm the key belongs to an account with `query:read` (Operator/Admin).

**Refresh fails after rotating the API key.**
Update the `ApiKey` parameter value in the dataset settings on the Power BI service (and locally in the `.pbix`). Republish if you changed it locally.

**Data is missing for a recently-fixed submission.**
The projection rebuilds on every submission save; there's no separate cache between Ingest and the feed. Just refresh the dataset.

**`$filter` returns a 400 "could not find a property named …".**
Check the casing — property names are PascalCase exactly as in the column tables (e.g. `SchemaName`, not `schemaName`).

Feed-specific gotchas (null columns, schema-metadata joins, the `Missing` status) live on the per-feed pages: **[samples.md → Troubleshooting](samples.md#troubleshooting)** and **[scorecard.md](scorecard.md)**.

## See also

- **[samples.md](samples.md)** — the raw data feed: full column reference, pre-filtering, data-model tips, and the JSON alternative.
- **[scorecard.md](scorecard.md)** — the RAG status board: function parameters, columns, and worked slices.
- **[schemas.md](schemas.md)** — the schema-metadata catalogue: labels, units, types, cadences and band edges to join onto `samples`.
- [`examples/powerbi/`](../../../examples/powerbi/README.md) — ready-made starter projects.
- [Excel integration](../excel.md) — the same feeds from Excel.
- [Explore page](../../admin-user-guide/explore.md) — the lightweight in-app charts (not a replacement for Power BI).

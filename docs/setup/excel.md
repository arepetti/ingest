# Excel integration (OData)

> **The cheapest analyst on-ramp.** If your team already has Microsoft 365, Excel can read the Ingest data directly through the same OData feed Power BI uses — no extra licence, no new tool to learn. Reach for Excel when an analyst wants to **poke at the numbers, pivot, and chart ad-hoc**. For shared, always-on dashboards, see [powerbi/](powerbi/README.md) (rich dashboards) or a free always-on option.

Ingest exposes its sample data as an **OData v4 feed** at `/odata/samples`. Excel's **Get & Transform** (Power Query) — built into Microsoft 365 and Excel 2016+ — consumes it directly. This guide walks through the recommended setup.

## What you get

The feed serves rows from the `SampleProjection` collection: **one row per sample** (not per submission), fully denormalised, so Excel gets readable, self-contained data without any joins.

The columns are identical to the Power BI feed — see [powerbi/samples.md § Columns](powerbi/samples.md#columns) for the full column table (`ServiceName`, `SchemaName`, `ValueName`, `ValueType`, the typed `*Value` columns, `Timestamp`, `Cadence`, `PeriodStart`/`PeriodEnd`, and the audit fields).

Server-side limits: page size 500, max `$top` of 5000 per request. Power Query handles paging transparently.

## Required role

The feed is gated by the **Operator** policy: any account with role `Operator` or `Admin` can read it. A `Service`-role key cannot.

Issue a dedicated **Operator** credential for reporting — revoking it later then doesn't affect anybody else. See the [admin guide](../admin-user-guide/accounts.md) for how to create one.

## Why "Anonymous + custom header"?

Like Power BI, Excel's OData connector doesn't natively support API-key auth — its credential dialog offers Anonymous, Windows, Basic, Web API, and Organizational account, none of which send an arbitrary `X-Api-Key` header. The supported workaround is to choose **Anonymous** and add the header through Power Query's `OData.Feed` `Headers` option, exactly as in [powerbi/README.md](powerbi/README.md#why-anonymous--custom-header). This works on refresh too.

## Setup (recommended: OData feed + header)

1. Open a blank workbook. On the **Data** tab, choose **Get Data → From Other Sources → From OData Feed**.
2. Enter the URL `https://ingest.example.org/odata/samples` and click **OK**.
3. On the credentials dialog, choose **Anonymous** and click **Connect**. *(You'll add the API key as a header in the next step.)*
4. In the Navigator, select **samples** and click **Transform Data** (not **Load**) to open the Power Query Editor.
5. On the **Home** tab click **Advanced Editor**. You'll see a source step like:
  ```m
   let
       Source = OData.Feed("https://ingest.example.org/odata/samples", null, [Implementation = "2.0"])
   in
       Source
  ```
   Add the `Headers` option so the key travels with every request:
6. Click **Done**, then **Close & Load**. Excel loads the data into a table on the sheet.

### Store the key in a parameter (do this — don't hard-code it)

Hard-coding the key inside the query means it lives in the `.xlsx` and travels with the file if you share it. Instead, define a Power Query **parameter** named `ApiKey` (the snippet above references it):

1. In the Power Query Editor, **Home → Manage Parameters → New Parameter**.
2. Name it `ApiKey`, **Type** = Text, **Current Value** = your operator key (`abc12345.7N3pK0M9C0LSx0OqGZpY3vW0eFkdsbVz...`).
3. Click **OK**, then make sure the source step uses `Headers = [ #"X-Api-Key" = ApiKey ]` (as above).

To rotate the key later, just update the parameter's value — no need to edit the query.

> Even with a parameter, treat the workbook as sensitive: anyone who can open it and edit queries can read the key. Share the *data* (e.g. a values-only copy or a published Power BI report), not the connected workbook, with people who shouldn't hold an operator key.

## Pre-filtering at the source

For large deployments, ask the server for less data by appending OData query options to the URL in the source step. The same filters documented for Power BI apply — see [powerbi/samples.md § Pre-filtering at the source](powerbi/samples.md#pre-filtering-at-the-source). For example, last 12 months for one service:

```m
Source = OData.Feed(
    "https://ingest.example.org/odata/samples?$filter=Timestamp ge 2025-06-01T00:00:00Z and ServiceName eq 'roads-team'",
    null,
    [ Implementation = "2.0", Headers = [ #"X-Api-Key" = ApiKey ] ]
)
```

Explicit filters in the source URL guarantee a smaller payload from the first request; Power Query also folds filters down when it can.

## Flatten the typed columns into one value

The projection has a separate column per type (`StringValue`, `IntegerValue`, `NumberValue`, `DateValue`, `BooleanValue`), so only the one matching each row's `ValueType` is populated and the rest are `null` — that's expected. For tidy pivots, collapse them into a single `Value` column. In the Power Query Editor, **Add Column → Custom Column**:

```m
= if [ValueType] = "Number"  then Number.From([NumberValue])  else
  if [ValueType] = "Integer" then Number.From([IntegerValue]) else
  if [ValueType] = "Date"    then DateTime.From([DateValue])  else
  if [ValueType] = "Boolean" then Logical.From([BooleanValue]) else
  [StringValue]
```

Then right-click and **Remove** the individual `*Value` columns to keep the table tidy, and **Close & Load**.

## Analyse with a PivotTable / PivotChart

1. Click any cell in the loaded table, then **Insert → PivotTable** (or **PivotChart** for a chart).
2. A useful starting layout for a KPI trend:
  - **Rows:** `PeriodStart` (or `Timestamp` grouped by month)
  - **Columns:** `ServiceName` — to compare services side by side
  - **Filters:** `SchemaName`, `ValueName`
  - **Values:** `Value` — set the summary to **Sum** or **Average** as appropriate
3. Use `Cadence` + `PeriodStart` for trends that already align to the cadence buckets the validator uses, so periods don't split awkwardly.

> Excel groups dates well, but for serious time-intelligence (year-to-date, same-period-last-year) Power BI's data model is stronger — see [powerbi/samples.md § Suggested data model](powerbi/samples.md#suggested-data-model).

## Refreshing

- **Manual:** **Data → Refresh All** (or right-click the table → **Refresh**).
- **On open / on a timer:** **Data → Queries & Connections → right-click the query → Properties**, then tick **Refresh data when opening the file** and/or **Refresh every N minutes**.
- **Unattended/scheduled refresh** isn't something desktop Excel does on its own — the workbook has to be open. If you need a dataset that refreshes on a schedule for others to consume, publish to the **Power BI service** instead ([powerbi/README.md § Refresh schedule](powerbi/README.md#refresh-schedule)) or drive a refresh with **Power Automate Desktop**.

## Alternative: From Web (header set in the dialog, no M editing)

If you'd rather not touch the Advanced Editor, you can use **Data → Get Data → From Other Sources → From Web**, switch to **Advanced**, enter the same `https://ingest.example.org/odata/samples` URL, and under **HTTP request header parameters** add a header named `X-Api-Key` with your key as the value. This avoids editing M, but `OData.Feed` (the method above) understands OData paging and metadata, so it's the better choice for anything beyond the first page.

## Alternative: the admin query endpoint (JSON)

If OData isn't an option, `POST /api/admin/query` returns the same projection as paged JSON. It's a POST with a JSON body, which is awkward to call from Excel's UI, so prefer the OData feed above for Excel. See [powerbi/samples.md § Alternative: the admin query endpoint](powerbi/samples.md#alternative-the-admin-query-endpoint) for the request shape if you script it.

## Troubleshooting

**"Access to the resource is forbidden" / 401.**
The header almost certainly isn't being sent. Open the source step in the Advanced Editor — your `OData.Feed` call must include the `Headers` option. A plain `OData.Feed("…")` carries no credentials.

**Refresh fails after rotating the API key.**
Update the `ApiKey` parameter's value (Power Query Editor → **Manage Parameters**), then **Refresh All**.

**Some columns are `null` for whole rows.**
Expected — only the `*Value` column matching the row's `ValueType` is populated. Use the flatten step above to collapse them into one `Value` column.

**My report needs schema metadata (label, unit, required).**
The flat projection denormalises only `SchemaName`/`ValueName`/`ServiceName`. Schema-level metadata lives at `/api/admin/schemas`; pull it as a second query (**From Web** against that endpoint with the same `X-Api-Key` header) and merge on the names in Power Query.

**It's slow / pulling too much.**
Pre-filter at the source (see above) so the server returns less, and remove columns you don't need early in the query.

## See also

- [powerbi/](powerbi/README.md) — the same feeds in Power BI, with the full column references, data-model tips, and scheduled refresh.
- [../admin-user-guide/accounts.md](../admin-user-guide/accounts.md) — creating the Operator credential this guide needs.
- [../client/api.md](../client/api.md) — the API surface, including `POST /api/admin/query`.


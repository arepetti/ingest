# PowerBI integration

> **This is the recommended, primary way to explore Ingest data.** The admin SPA's dashboard and the built-in reports are deliberately basic (a health check and a developer-authored convenience respectively). For real analysis — filtering, slicing, trends, charts, large datasets — connect a BI tool to the OData feed described here.

Ingest exposes its sample data as an **OData feed** at `/odata/samples`. Any OData v4 client can consume it; this guide walks through the most common case — Microsoft Power BI Desktop.

## What you get

The feed serves rows from the `SampleProjection` collection: one row **per sample** (not per submission). Each row is fully denormalised, so PowerBI gets readable, self-contained data without any joins to perform.

| Column            | Type                | Notes |
|-------------------|---------------------|-------|
| `Id`              | `Edm.Guid`          | Projection row id. |
| `SubmissionId`    | `Edm.Guid`          | The parent submission. Use this to roll up samples that arrived together. |
| `ServiceAccountId`| `Edm.Guid`          | Owning account id. |
| `ServiceName`     | `Edm.String`        | Machine-style account name (e.g. `roads-team`). |
| `SchemaName`      | `Edm.String`        | Schema name. |
| `ValueName`       | `Edm.String`        | Value name inside the schema. |
| `ValueType`       | `Edm.String`        | One of `String`, `Integer`, `Number`, `Date`, `Boolean`. |
| `StringValue`     | `Edm.String?`       | Populated when `ValueType = String`. |
| `IntegerValue`    | `Edm.Int64?`        | Populated when `ValueType = Integer`. |
| `NumberValue`     | `Edm.Double?`       | Populated when `ValueType = Number`. |
| `DateValue`       | `Edm.DateTimeOffset?` | Populated when `ValueType = Date`. |
| `BooleanValue`    | `Edm.Boolean?`      | Populated when `ValueType = Boolean`. |
| `Timestamp`       | `Edm.DateTimeOffset` | When the sample was measured. |
| `Note`            | `Edm.String?`       | Free-form note attached to the sample. |
| `Cadence`         | `Edm.String`        | `Daily` / `Weekly` / `Fortnightly` / `Monthly` / `Quarterly` / `SemiAnnually` / `Yearly`. |
| `PeriodStart`     | `Edm.DateTimeOffset` | Inclusive bucket start matching the cadence. |
| `PeriodEnd`       | `Edm.DateTimeOffset` | Exclusive bucket end matching the cadence. |
| `CreatedAt` / `CreatedBy` / `ModifiedAt` / `ModifiedBy` / `IsDeleted` | … | Standard audit fields. Soft-deleted rows are excluded server-side. |

Server-side limits: page size 500, max `$top` of 5000 per request. Power BI handles paging transparently.

## Required role

The feed is gated by the **Operator** policy: any account with role `Operator` or `Admin` can read it. A `Service`-role key cannot.

Issue a dedicated Operator-kind credential for the report — that way revoking it later doesn't affect anybody else. See the [admin guide](../admin-user-guide/accounts.md) for how to create one.

## Power BI Desktop (one-time setup)

1. Open Power BI Desktop and choose **Get Data → OData feed**.
2. Switch to **Advanced** and enter:
   - **URL parts:** `https://ingest.example.org/odata/samples`
   - Leave the query options empty unless you want to pre-filter (more on that below).
3. On the credentials dialog, choose **Anonymous**. *(You'll add the API key as a custom header on the next step.)*
4. Click **Connect**. Power BI lists the entity set; expand `samples` and click **Transform Data** to open the Power Query editor.
5. In the editor, find the source step (`= OData.Feed("https://ingest.example.org/odata/samples", null, [Implementation="2.0"])`) and **add headers**:

   Replace it with:

   ```m
   Source = OData.Feed(
       "https://ingest.example.org/odata/samples",
       null,
       [
           Implementation = "2.0",
           Headers = [ #"X-Api-Key" = "abc12345.7N3pK0M9C0LSx0OqGZpY3vW0eFkdsbVz..." ]
       ]
   )
   ```

   Replace the placeholder with your actual operator key.

   > **Don't hard-code the key into a shared PBIX.** Use a [Power Query parameter](https://learn.microsoft.com/power-query/power-query-query-parameters) and reference it as `Headers = [ #"X-Api-Key" = ApiKey ]`. When publishing to the Power BI service, set the parameter value at workspace level.

6. Click **Close & Apply**. PowerBI loads the data and you can start building visuals.

### Why "Anonymous + custom header"?

PowerBI's OData connector doesn't natively support API-key auth (it offers Anonymous, Windows, Web API key for Excel-style endpoints, etc.). Adding the header through `OData.Feed`'s `Headers` option is the supported workaround and works with refresh in the Power BI service as long as you store the key in a parameter or via a personal/enterprise gateway.

## Pre-filtering at the source

For large deployments it pays to ask the server for less data. The OData query options are honoured:

| Want | URL |
|------|-----|
| Last 12 months only | `…/odata/samples?$filter=Timestamp ge 2025-06-01T00:00:00Z` |
| One service | `…/odata/samples?$filter=ServiceName eq 'roads-team'` |
| One schema | `…/odata/samples?$filter=SchemaName eq 'monthly_kpis'` |
| Only one value | `…/odata/samples?$filter=SchemaName eq 'monthly_kpis' and ValueName eq 'tonnes'` |
| Newest first | `…/odata/samples?$orderby=Timestamp desc` |
| Field selection | `…/odata/samples?$select=ServiceName,SchemaName,ValueName,NumberValue,Timestamp` |

Combine freely; PowerBI also pushes filters down when you fold them in Power Query, but explicit filters in the source URL guarantee a smaller payload from request 1.

## Suggested data model

Power BI handles the long-format table well as-is, but for cleaner charts you'll often want to:

1. Split the column `Value` into a single numeric/date/text column. In Power Query:
   ```m
   #"Added Value" = Table.AddColumn(#"Renamed Columns", "Value", each
       if [ValueType] = "Number"  then Number.From([NumberValue])  else
       if [ValueType] = "Integer" then Number.From([IntegerValue]) else
       if [ValueType] = "Date"    then DateTime.From([DateValue])  else
       if [ValueType] = "Boolean" then Logical.From([BooleanValue]) else
       [StringValue])
   ```
2. Hide the individual `*Value` columns from the report view to keep things tidy.
3. Create a **calendar table** and relate it to `Timestamp` for time-intelligence functions (`DATESYTD`, `SAMEPERIODLASTYEAR`, etc.).
4. Use `Cadence` + `PeriodStart` for trend visuals that already align to the cadence buckets the validator uses.

## Refresh schedule

After publishing to the Power BI service:

1. In the dataset settings, set **Data source credentials → Authentication method = Anonymous**. (The custom header is set by the Power Query script.)
2. Set the **Refresh schedule** to whatever cadence fits your data; even Daily is usually fine because the feed is small per sample.
3. If you're behind a private network, add an **on-premises data gateway** and add the OData source to it.

## Troubleshooting

**"Unable to connect" / 401 from PowerBI.**
Most often the header isn't being sent. Open the source step in Power Query — your `OData.Feed` call must have the `Headers` option set. Plain `OData.Feed("…")` won't carry credentials.

**Refresh fails after rotating the API key.**
Update the parameter value in the dataset settings on the Power BI service (and the local PBIX). Republish if you changed it locally.

**Sample data is missing for a recently-fixed submission.**
The projection rebuilds on every submission save; if you fixed a submission via the admin API, refresh the dataset. There is no separate cache between Ingest and the OData feed.

**Some columns show as `null` for entire rows.**
That's expected. The projection has separate columns for each type so PowerBI gets correct typing — only the column matching the row's `ValueType` is populated. Use the split-into-single-column step above to flatten.

**My report needs joins to schema metadata.**
The flat projection deliberately denormalises `SchemaName`/`ValueName`/`ServiceName` — schema-level metadata like *label*, *unit*, *required* lives only in `/api/admin/schemas`. Pull that endpoint into a second query (Web data source pointing at `/api/admin/schemas`) and join on the names in Power Query.

## Alternative: the admin query endpoint

If OData isn't your thing, `POST /api/admin/query` returns the same projection as a paged JSON document. The body shape is:

```json
{
  "serviceIds":   ["…", "…"],
  "schemaNames":  ["monthly_kpis"],
  "from":         "2026-01-01T00:00:00Z",
  "to":           "2026-12-31T23:59:59Z",
  "latestOnly":   false,
  "includeDeleted": false,
  "page":         1,
  "pageSize":     500,
  "sort":         "timestamp"
}
```

Same role requirement (Operator or higher), same `X-Api-Key` header. The response is a standard `PagedResponse<SampleProjectionDto>` with the same columns as the OData feed.

Use this from custom dashboards (Grafana plugins, n8n flows, Excel via Power Query Web) when an OData connector isn't available.

# Explore

> **Explore is not a BI tool.** It's a deliberately small, in-app convenience for getting a quick
> read on your numeric KPIs without leaving the admin console — a few charts, a comparison, a
> snapshot table. It has no pivoting, no cross-schema analysis, no calculated measures and no large
> result sets.
>
> **The primary way to explore the data is PowerBI (or any similar BI/OData client) pointed at the
> OData feed** — see [setup/powerbi.md](../setup/powerbi.md). That's where real filtering, slicing,
> charting and big datasets belong. Reach for Explore when you just want a fast look from inside the
> console (handy when a deployment has no BI tooling or a tight budget); reach for PowerBI for
> everything else.

The **Explore** page (operators and admins) charts the numeric values of a single schema, broken
down by reporting period and by service. Everything is aggregated **server-side** — the browser
never downloads raw samples — so it stays responsive at the reference data volumes (see
[setup/performance.md](../setup/performance.md)).

## The filter bar

Every view shares one filter bar at the top. The current selection is encoded in the page URL, so
you can bookmark a view or paste the link to a colleague and they'll land on exactly the same
chart.

| Filter        | What it does |
|---------------|--------------|
| **Schema**    | The schema to explore. Only one at a time. |
| **Value**     | Which numeric value to chart (Trend and Compare only). Only `Number` and `Integer` values appear — text, date and boolean values can't be aggregated. |
| **Services**  | Restrict to one or more services. Empty means *all services*. |
| **Aggregation** | How the samples in each period bucket are reduced: **Average**, **Sum**, **Minimum**, **Maximum**, or **Sample count**. |
| **Period**    | A relative range (last day / week / month) or a custom from/to window. *All time* by default. |

Samples are grouped into buckets by each value's own **cadence** (Daily, Weekly, Monthly, …), the
same boundaries the rest of the system uses. Submissions from different services in the same window
collapse onto the same bucket automatically.

## The three views

### Trend

A line chart of the chosen value over time, **one line per service**. Toggle **Combine services**
to collapse them into a single line for the whole registry. Use this to answer "is this number
going up or down?".

The **Average** aggregation across the whole range is an exact, count-weighted mean (each period is
weighted by how many samples it held), not a naive average of the per-period averages.

Toggle **Projection** to extend the chart by the next two periods. Each line gains a dashed
continuation fitted from its own history (a simple straight-line / least-squares trend), and in
per-service mode a single grey **Overall trend** line shows the aggregate direction. It's a rough
"if the recent trend continued" guide for conversation, not a forecast — it ignores seasonality and
one-off spikes, so treat it as indicative only.

### Compare

A horizontal bar chart, **one bar per service**, ranking services by the chosen aggregation over the
whole selected period. Use this to answer "who's highest / lowest?".

### Snapshot

A table of the **latest period's value for every service and every numeric value** on the schema.
Each column header shows the value and which period it's reporting. Use this as a current-state
scoreboard.

## Stat cards

Above the Trend and Compare charts a row of cards summarises the current selection: the overall
aggregate, the latest period's figure, the total number of samples, and how many periods and
services are in scope.

## Reading the numbers another way

- **View as table.** Each chart has a **View as table** toggle that swaps the chart for the same
  numbers in a grid — useful for copy-pasting or for screen-reader users.
- **Export CSV (this view).** The **⋮** menu exports exactly what the active view shows
  (per-period per-service for Trend, per-service for Compare, the latest-value matrix for Snapshot).
- **Export chart (PNG).** Saves the current Trend or Compare chart as an image for a slide or email.

## Limits and caveats

- **Numeric values only.** String, date and boolean values are skipped — there's nothing to chart.
- **One schema at a time.** There is no cross-schema view by design.
- **Capped result set.** The query is bounded (well above the reference volume for one schema over a
  couple of years). If you somehow exceed it, narrow the period or the service list — or, better,
  use PowerBI for analysis at that scale.
- **Aggregated, not raw.** Explore shows reduced figures per period. To see or export individual
  samples, use the [submissions](submissions.md) page, the OData feed, or `POST /api/admin/query`.

## API

The page is backed by a single read-only endpoint (operator/admin):

```
GET /api/admin/explore/series
    ?schema={name}            # required
    &value={valueName}        # repeatable; omit for every numeric value
    &serviceIds={guid}        # repeatable; omit for every service
    &from={iso}&to={iso}      # optional half-open window [from, to)
    &agg={Average|Sum|Min|Max|Count}   # defaults to Average
```

It returns one timeline per in-scope numeric value, each as a list of cadence buckets carrying the
overall reduced value plus a per-service breakdown, along with the resolved service list. A `404`
means no schema with that name. This is the same endpoint the SPA calls, so you can drive it from
your own tooling if you'd rather not build against OData for a simple chart.

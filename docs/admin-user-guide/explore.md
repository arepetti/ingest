# Explore

> **Explore is not a BI tool.** It's a deliberately small, in-app convenience for getting a quick
> read on your numeric KPIs without leaving the admin console — a few charts, a comparison, a
> snapshot table and a RAG status board. It has no pivoting, no calculated measures and no large
> result sets.
>
> **The primary way to explore the data is PowerBI (or any similar BI/OData client) pointed at the
> OData feed** — see [setup/powerbi/](../setup/powerbi/README.md). That's where real filtering, slicing,
> charting and big datasets belong. Reach for Explore when you just want a fast look from inside the
> console (handy when a deployment has no BI tooling or a tight budget); reach for PowerBI for
> everything else.

The **Explore** page (operators and admins) charts the numeric values of a single schema, broken
down by reporting period and by service. Everything is aggregated **server-side** — the browser
never downloads raw samples — so it stays responsive at the reference data volumes (see
[setup/performance.md](../setup/performance.md)).

## Layout: two levels of tabs

Right below the page title sit two **outer tabs**:

- **Scorecard** — a cross-schema Red/Amber/Green status board (see [Scorecard](#scorecard)).
- **Analysis** — the per-schema charts, with three **inner tabs**: **Trend**, **Compare services**
  and **Snapshot**.

The filter bar and the inner tabs only appear under **Analysis**; the Scorecard keeps just the
**Services** filter plus its own **Show** / **Period** options (see [Scorecard](#scorecard)).

## The filter bar

The Analysis views share one filter bar. The current selection (including the active tabs and view
toggles) is encoded in the page URL, so you can bookmark a view or paste the link to a colleague and
they'll land on exactly the same chart.

| Filter        | What it does |
|---------------|--------------|
| **Schema**    | The schema to explore. Only one at a time. |
| **Value**     | Which numeric value to chart (Trend and Compare only). Only `Number` and `Integer` values appear — text, date and boolean values can't be aggregated. |
| **Services**  | Restrict to one or more services. Empty means *all services*. |
| **Aggregation** | How the samples in each period bucket are reduced: **Average**, **Sum**, **Minimum**, **Maximum**, or **Sample count**. |
| **Period**    | A relative range (last day / week / month) or a custom from/to window. *All time* by default. |
| **Compare with previous** | (Trend only) Overlay the same selection shifted back in time — pick **1 month**, **6 months**, or **1 year**, or **No** to turn it off (the default). Needs a Period range (it's unavailable for *All time*); the two windows are allowed to overlap. |

Samples are grouped into buckets by each value's own **cadence** (Daily, Weekly, Monthly, …), the
same boundaries the rest of the system uses. Submissions from different services in the same window
collapse onto the same bucket automatically.

The **Scorecard** tab is cross-schema, so it drops the Analysis filters and keeps only **Services**,
plus its own **Show** and **Period** controls.

### Presets

The **Presets** dropdown (top right) saves the whole current selection — every filter and view
toggle — under a name you type, so you can jump back to a view you use often. Pick a preset to reload
it, or use the trash icon next to its name to delete it (no confirmation). You can keep up to **five**
presets. Unlike the shareable URL, presets are stored in your browser's local storage, so they're
per-browser and not shared when you paste the link to someone else.

## The views

### Trend

A line chart of the chosen value over time, **one line per service**. Toggle **Combine services**
to collapse them into a single line for the whole registry. Use this to answer "is this number
going up or down?".

The **Average** aggregation across the whole range is an exact, count-weighted mean (each period is
weighted by how many samples it held), not a naive average of the per-period averages.

Turn on **Compare** to read this period against an earlier one: each line gains a faded, dashed
counterpart drawn from the same selection shifted back by the chosen amount (1 month / 6 months / 1
year). The two windows are aligned period-for-period — the first bucket of "now" sits above the first
bucket of "then" — so you can eyeball this-vs-last even when the calendar dates differ. Overlapping
ranges are allowed; it's up to you whether that's meaningful.

Toggle **Projection** to extend the chart by the next two periods. Each line gains a dashed
continuation fitted from its own history (a simple straight-line / least-squares trend), and in
per-service mode a single grey **Overall trend** line shows the aggregate direction. It's a rough
"if the recent trend continued" guide for conversation, not a forecast — it ignores seasonality and
one-off spikes, so treat it as indicative only.

If the charted value has a [RAG target band](schemas.md#the-rag-target-band) (Green / Amber ranges on
the schema), it's drawn behind the lines as a **green** ideal zone with **amber** shoulders, so you can
see at a glance whether services are sitting where they should. Anything outside the amber range (the
"red" zone) is left unshaded. The band is purely a visual reference; it's never enforced and doesn't
affect any of the aggregated figures. It also appears on the historical-data view.

### Compare

A horizontal bar chart, **one bar per service**, ranking services by the chosen aggregation over the
whole selected period. Use this to answer "who's highest / lowest?".

### Snapshot

A table of the **latest period's value for every service and every numeric value** on the schema.
Each column header shows the value and which period it's reporting. Use this as a current-state
scoreboard.

### Scorecard

A cross-schema **Red / Amber / Green status board**, on its own outer tab. Unlike the Analysis views
it isn't tied to a single schema: it sweeps **every enabled schema** and surfaces only the numeric
values that carry a [RAG target band](schemas.md#the-rag-target-band). Indicators are grouped under
each schema's label, then under each value, with one card per reporting service.

Each card shows the service, its reported value (and the date that period started) and a coloured
dot:

- **Green — on target:** the value sits inside the ideal range.
- **Amber — warning:** inside the acceptable range but outside the ideal range (or there's only an
  acceptable range defined, with no narrower ideal).
- **Red — off target:** outside the acceptable range.
- **Grey — no submission:** the service was expected to report but didn't (only in *Last period*
  mode, see below). Grey cards aren't clickable.

Clicking a coloured card opens the underlying submission in view mode.

#### Show: which sample each card reflects

The **Show** dropdown chooses what a card represents:

- **Latest available** (default) — each service's **most recent** submission for the value, however
  old it is. Services that have never reported a banded value are left out. This is the original
  behaviour: the board only contains cards that have a value.
- **Last period** — a single period (chosen by **Period**, below). Every service the schema **applies
  to** gets a card: a coloured one if it submitted that period, or a grey **no submission** card if it
  didn't. A service only ever sees cards for schemas available to it (global schemas reach everyone;
  restricted schemas only their listed services), so a service never appears under a schema it can't
  submit. Because non-reporters are shown, schemas and values stay visible even when **nobody** has
  submitted the period yet — the whole board reads as a "who's reported?" checklist.

When **Show** is *Last period*, a second **Period** dropdown appears:

- **Current** (default) — the period that contains today, even though it's still open.
- **Latest closed** — the most recent period that has fully elapsed (the one before the current).

Each value uses its own [cadence](schemas.md) to work out what "the period" is, so a monthly KPI and
a weekly KPI on the same board are each judged against their own calendar.

Toggle **Hide on-target** (top right of the board) to drop every green card and show only what needs
attention — ambers, reds **and** grey "no submission" cards all stay visible; values and schemas left
with nothing to show are hidden. It's off by default.

The classification mirrors exactly how the band is shaded on the Trend chart. Values without a band
and disabled schemas are always left out, so the board stays focused on KPIs you've actually set
targets for. Use the **Services** filter to narrow the board to a team or directorate; in *Last
period* mode the filter also narrows who is expected to report (disabled services are never expected).
The selected **Show** and **Period** options are part of the shareable URL and can be saved as a
preset.

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
- **One schema at a time**, except the Scorecard view, which is cross-schema but shows only the RAG
  status (latest available, or a single period) of values that carry a target band.
- **Capped result set.** The query is bounded (well above the reference volume for one schema over a
  couple of years). If you somehow exceed it, narrow the period or the service list — or, better,
  use PowerBI for analysis at that scale.
- **Aggregated, not raw.** Explore shows reduced figures per period. To see or export individual
  samples, use the [submissions](submissions.md) page, the OData feed, or `POST /api/admin/query`.

## API

The page is backed by two read-only endpoints (operator/admin).

The Trend, Compare and Snapshot views use:

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
means no schema with that name.

The Scorecard view uses:

```
GET /api/admin/explore/scorecard
    ?serviceIds={guid}        # repeatable; omit for every service
    &mode={LatestAvailable|LastPeriod}   # defaults to LatestAvailable
    &period={Current|LatestClosed}       # used only when mode=LastPeriod; defaults to Current
```

In `LatestAvailable` mode it returns every enabled schema that has at least one banded numeric value
with data, and for each reporting service a cell with its value, period and a `status` of
`Green` / `Amber` / `Red`. In `LastPeriod` mode it instead emits, per banded value, one cell for every
service the schema applies to: a classified cell if the service submitted the chosen period, or a
**missing** cell (`status`, `value` and `submissionId` all `null`, with the period it was expected
for) if it didn't — so schemas with no submissions at all still appear.

These are the same endpoints the SPA calls, so you can drive them from your own tooling if you'd
rather not build against OData for a simple chart.

For BI tools, the same scorecard is also published as a flat OData function at
`/odata/scorecard(mode,period)` — one row per (schema, value, service) cell, including the target
band edges. See [PowerBI integration → Scorecard feed](../setup/powerbi/scorecard.md).

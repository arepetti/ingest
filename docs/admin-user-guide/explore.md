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

Right below the page title sit three **outer tabs**:

- **Scorecard** — a cross-schema Red/Amber/Green status board (see [Scorecard](#scorecard)).
- **Analysis** — the per-schema charts, with three **inner tabs**: **Trend**, **Compare services**
  and **Snapshot**.
- **Anomalies** — a cross-schema board flagging values that deviate from their own recent history for
  the current (or latest closed) period (see [Anomalies](#anomalies)).

The Analysis filter bar and the inner tabs only appear under **Analysis**; the Scorecard and
Anomalies tabs keep just the **Services** filter plus their own options.

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

#### Event overlay

When you hold the `events:read` capability, the Trend chart also overlays the [Events
timeline](events.md) on top of your data, so you can eyeball "did that dip line up with the
maintenance window?" without leaving the chart:

- **Point in time** events draw as a **solid vertical bar** at the bucket their timestamp falls
  in, labelled with the event's title.
- **From now on** events draw as a **thicker vertical bar** topped with a small
  right-pointing arrowhead, signalling they run on indefinitely rather than stopping at a known
  point.
- **Interval** events draw as a full-height **shaded band** (top to bottom of the chart) spanning
  every bucket the event's `[start, start + duration]` window overlaps.

Each kind keeps the same colour it has on the [Events page](events.md#the-three-kinds) (avatar
colours), and a small legend above the chart lists only the kinds actually present in the current
view. The **Show events** switch (next to **View as table**) turns the overlay off if it's getting
in the way; it's on by default.

An event only appears if it applies to at least one currently in-scope service (or has no service
scope at all, i.e. "all services") **and** its span overlaps the chart's plotted period — an event
entirely before or after the visible range, or affecting only services you've filtered out, is
simply omitted. The overlay has no effect on any exported/aggregated figure; it's a purely visual
annotation, same as the RAG target band described above. It only appears on the Trend chart in the
default (non-table) view — the **Compare** and **Snapshot** views, and **View as table**, don't
chart a time axis so there's nowhere to anchor it.

> Events shown here are capped at the first 500 matching the current period filter — comfortably
> above the reference volume for an admin-curated annotation timeline. For anything larger, or to
> build your own overlay, pull the [`events` OData feed](../setup/powerbi/events.md) instead.

#### Highlight anomalies

The **Highlight anomalies** dropdown button (rightmost in the Trend toolbar) rings the points that
deviate strongly from each line's own recent history. Each drawn line is scored independently: every
bucket is compared against the values that **precede** it (so the marker answers "was this surprising
*given what came before*?"). Flagged points get a hollow amber ring, the tooltip shows the **z-score**
alongside the value, and a small counter above the chart tallies how many were highlighted.

Open the dropdown to switch the highlight on/off and tune the detector:

- **History window** — how many preceding periods form the baseline (8 / 12 / 26). Gaps don't count:
  a period a service didn't report is simply absent from its history, never treated as a zero.
- **Sensitivity (|z| threshold)** — how far from the baseline a value must sit to be flagged
  (2 / 2.5 / 3). Lower flags more.
- **Robust (median / MAD)** — switch the baseline from mean + standard deviation to median + median
  absolute deviation, which resists a handful of past spikes inflating the baseline and hiding later
  outliers.

A point needs at least **four** preceding values before it can be scored, so the start of a short
series is never flagged. This is a **view aid only** — it never rejects a submission and stores
nothing. For hard, history-aware rejections use a schema [validation rule](validation.md) with
`previous()` / `latest()`, which runs in the submission pipeline.

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

### Anomalies

A cross-schema **anomaly board**, on its own outer tab. It answers a different question from the
Scorecard: not "is this value on target?" but "is this value **unusual for itself**?". For one period
it scores every applicable service's value against that service's own recent history (the same z-score
/ MAD detector as the Trend [Highlight anomalies](#highlight-anomalies) toggle), and lays the result
out as a card board grouped by schema then value.

Each card shows the service, its value (and z-score) and a coloured dot:

- **Green — no anomalies:** the value submitted for the period is in line with its recent history (or
  there wasn't enough history yet to judge — fewer than four preceding periods).
- **Yellow — anomaly:** the value deviates strongly from its recent history.
- **Grey — no submission:** the service was expected to report this period but didn't. Grey cards
  aren't clickable.

**Clicking a card jumps straight to the Analysis → Trend chart** for that schema, value and service,
with **Highlight anomalies** already on and the same window / sensitivity / robust settings carried
across — so you land on exactly the chart that flagged it.

The board's filter bar offers:

- **Schemas** — which schemas to scan (multi-select). Empty scans **every** schema with numeric
  values. (Unlike the Scorecard, a value does **not** need a RAG band to appear here.)
- **Services** — restrict to a team or directorate; this also narrows who is *expected* to report
  (so the grey "no submission" cards reflect only the services you care about).
- **Period** — **Current** (the open period) or **Latest closed** (the last fully-elapsed one). Each
  value uses its own [cadence](schemas.md) to decide what "the period" is.

The detector tuning (**History window**, **Sensitivity**, **Robust** — the same controls as the Trend
view's Anomalies popover) sits inline in a row directly under those dropdowns.

Each schema's card is **collapsible** (click the header). Cards start expanded, except ones where
**every** cell is a grey "no submission" — those start collapsed, since there's nothing to act on
yet; a one-line summary (e.g. *2 anomalies · 5 normal · 1 no submission*) shows on the collapsed
header so you can still scan the board.

Toggle **Hide normal** (top right) to drop the green cards and keep only anomalies and grey "no
submission" cards. Everything — the schema selection, period and detector settings — is part of the
shareable URL and can be saved as a preset.

## Stat cards

Above the Trend and Compare charts a row of cards summarises the current selection: the overall
aggregate, the latest period's figure, the total number of samples, and how many periods and
services are in scope.

## Reading the numbers another way

- **View as table.** Each chart has a **View as table** toggle that swaps the chart for the same
  numbers in a grid — useful for copy-pasting or for screen-reader users.
- **Export CSV (this view).** The **⋮** menu exports exactly what the active view shows
  (per-period per-service for Trend, per-service for Compare, the latest-value matrix for Snapshot).
  With **Highlight anomalies** on in **Combine services** mode, the Trend export (and the table view)
  gain **z** and **Anomaly** columns for the single overall line.
- **Export chart (PNG).** Saves the current Trend or Compare chart as an image for a slide or email.

## Limits and caveats

- **Numeric values only.** String, date and boolean values are skipped — there's nothing to chart.
- **One schema at a time**, except the Scorecard and Anomalies boards, which are cross-schema (the
  Scorecard shows RAG status for banded values; Anomalies flags values that deviate from their own
  recent history).
- **Capped result set.** The query is bounded (well above the reference volume for one schema over a
  couple of years). If you somehow exceed it, narrow the period or the service list — or, better,
  use PowerBI for analysis at that scale.
- **Aggregated, not raw.** Explore shows reduced figures per period. To see or export individual
  samples, use the [submissions](submissions.md) page, the OData feed, or `POST /api/admin/query`.

## API

The page is backed by three read-only endpoints (operator/admin).

The Trend, Compare and Snapshot views use:

```
GET /api/admin/explore/series
    ?schema={name}            # required
    &value={valueName}        # repeatable; omit for every numeric value
    &serviceIds={guid}        # repeatable; omit for every service
    &from={iso}&to={iso}      # optional half-open window [from, to)
    &agg={Average|Sum|Min|Max|Count}   # defaults to Average
    &anomaly=true             # opt-in: score each bucket for anomalies
    &anomalyWindow={int}      # preceding periods in the baseline; defaults to 12 (clamped server-side)
    &anomalyThreshold={num}   # |z| cutoff to flag; defaults to 2.5 (clamped server-side)
    &anomalyRobust=true       # use median/MAD instead of mean/standard deviation
```

It returns one timeline per in-scope numeric value, each as a list of cadence buckets carrying the
overall reduced value plus a per-service breakdown, along with the resolved service list. When
`anomaly=true`, each bucket and per-service point also carries a `z` score and an `isAnomaly` flag
(both unset otherwise). A `404` means no schema with that name.

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

The Anomalies board uses:

```
GET /api/admin/explore/anomalies
    ?schema={name}            # repeatable; omit to scan every enabled schema
    &serviceIds={guid}        # repeatable; omit for every service
    &period={Current|LatestClosed}   # defaults to Current
    &window={int}             # preceding periods in the baseline; defaults to 12 (clamped server-side)
    &threshold={num}          # |z| cutoff to flag; defaults to 2.5 (clamped server-side)
    &robust=true              # use median/MAD instead of mean/standard deviation
```

It returns, per scanned schema and numeric value, one cell for every service the schema applies to:
a classified cell (`state` of `Normal` or `Anomaly`, plus `value`, `z` and `submissionId`) if the
service submitted the target period, or a **missing** cell (`state`, `value`, `z` and `submissionId`
all `null`) if it didn't. A submitted value with too little history to score is `Normal` with a `null`
`z`.

These are the same endpoints the SPA calls, so you can drive them from your own tooling if you'd
rather not build against OData for a simple chart.

For BI tools, the same scorecard is also published as a flat OData function at
`/odata/scorecard(mode,period)` — one row per (schema, value, service) cell, including the target
band edges. See [PowerBI integration → Scorecard feed](../setup/powerbi/scorecard.md).

The Trend chart's [event overlay](#event-overlay) reuses the existing `GET /api/admin/events`
endpoint (see [Events → API](events.md#api)) rather than a dedicated Explore endpoint; BI tools get
the same data from the [`events` OData feed](../setup/powerbi/events.md).

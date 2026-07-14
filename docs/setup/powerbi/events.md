# Events feed (annotations timeline)

The `events` feed is a **flat, one-row-per-event** feed over the admin-recorded events timeline —
maintenance windows, incidents, deployments and other "things that happened" that give context to
the numbers in the [samples](samples.md)/[scorecard](scorecard.md) feeds. It's the same data behind
the admin **Events** page (see the [admin user guide](../../admin-user-guide/events.md)) and the
event overlay on the [Explore page](../../admin-user-guide/explore.md#event-overlay)'s Trend chart.

```
/odata/events
```

> **First time here?** Authentication, the custom-header recipe, the `ApiKey` parameter, query options and scheduled refresh are all on the **[hub page](README.md)**. This page covers only what's specific to the events feed.

## Required role

Unlike `samples`/`scorecard` (which gate on `query:read`), the events feed requires the
**`events:read`** capability — the same one that gates the admin Events page. It is **not** granted
to Operator/Admin by default the way `query:read`/`schemas:read` are; an administrator has to assign
it explicitly to the credential you use for reporting (see [custom capabilities](../../admin-user-guide/accounts.md)).

## What you get

**One row per live (non-deleted) event.** Events are a small, admin-curated annotation set (not bulk
telemetry), so the whole list is materialised in a single request.

| Column            | Type                  | Notes |
|-------------------|-----------------------|-------|
| `Id`              | `Edm.Guid`            | The key. |
| `Timestamp`       | `Edm.DateTimeOffset`  | The instant the event occurred, or the **start** instant for `Interval`/`FromNowOn` events. |
| `Label`           | `Edm.String`          | Short title. |
| `Description`     | `Edm.String?`         | Optional longer free-text description. |
| `Kind`            | `Edm.String`          | `PointInTime`, `Interval`, or `FromNowOn` — see below. |
| `DurationMinutes` | `Edm.Int32?`          | Only set (and only meaningful) when `Kind` is `Interval`. |
| `EffectiveEnd`    | `Edm.DateTimeOffset?` | The computed end of the event's span — see **[Querying by open/closed interval](#querying-by-openclosed-interval)**. |
| `ServiceIds`      | collection of `Edm.Guid` | Services this event affects; **empty means "all services"**. |
| `CreatedAt` / `CreatedBy` / `ModifiedAt` / `ModifiedBy` | audit columns | Standard audit trail (`CreatedBy`/`ModifiedBy` are display names, may be `null`). |

### The three kinds

| `Kind`        | Meaning | `EffectiveEnd` |
|---------------|---------|-----------------|
| `PointInTime` | A single instant (e.g. a deployment). | Equal to `Timestamp` (a zero-length span). |
| `Interval`    | A bounded window (e.g. a maintenance window). Always carries a `DurationMinutes`. | `Timestamp + DurationMinutes`. |
| `FromNowOn`   | An open-ended span starting at `Timestamp` and running indefinitely (e.g. "service X decommissioned"). | `null` — there is no end. |

## Querying by open/closed interval

Because `FromNowOn` events have no end, "does this event overlap the window I care about?" can't be
expressed with a single `Timestamp` comparison alone. `EffectiveEnd` makes it a plain, three-part
`$filter` that works uniformly across all three kinds:

```
Timestamp le <windowEnd> and (EffectiveEnd eq null or EffectiveEnd ge <windowStart>)
```

`EffectiveEnd eq null` covers the open-ended `FromNowOn` case (it always overlaps any window that
starts on/after its own start, since it never ends); the other branch handles `PointInTime` and
`Interval` events, whose span is fully known.

| Want | URL |
|------|-----|
| Events overlapping March 2026 | `…/odata/events?$filter=Timestamp le 2026-04-01T00:00:00Z and (EffectiveEnd eq null or EffectiveEnd ge 2026-03-01T00:00:00Z)` |
| Only maintenance-style (interval) events | `…/odata/events?$filter=Kind eq 'Interval'` |
| Only events affecting a specific service | `…/odata/events?$filter=ServiceIds/any(s: s eq 11111111-1111-1111-1111-111111111111)` |
| Only "all services" events | `…/odata/events?$filter=ServiceIds/$count eq 0` |
| Events still open (no known end) | `…/odata/events?$filter=EffectiveEnd eq null` |
| Lean columns | `…/odata/events?$select=Timestamp,Label,Kind,EffectiveEnd` |

> **Service scope.** `ServiceIds/any(s: s eq <guid>)` matches events explicitly scoped to that
> service; `ServiceIds/$count eq 0` matches "all services" events (an empty collection). Combine
> both with `or` to get "applies to this service" including the global ones.

## Power BI source

Point a query at the feed, reusing the [header recipe](README.md#connecting-power-bi-desktop) and your `ApiKey` / `BaseUrl` parameters:

```m
Source = OData.Feed(
    BaseUrl & "/odata/events",
    null,
    [ Implementation = "2.0", Headers = [ #"X-Api-Key" = ApiKey ] ]
)
```

A common use is annotating a `samples`-driven trend visual: load `events`, filter/expand to the
window your chart covers, and add the results as **vertical reference lines** (`PointInTime`/
`FromNowOn`) or a **shaded band** (`Interval`, from `Timestamp` to `EffectiveEnd`) — the same
treatment the in-app [Explore page](../../admin-user-guide/explore.md#event-overlay) gives them.

## See also

- **[samples.md](samples.md)** — the raw data feed events give context to.
- **[Admin user guide → Events](../../admin-user-guide/events.md)** — creating and managing events from the admin SPA.
- **[Explore page → Event overlay](../../admin-user-guide/explore.md#event-overlay)** — the same annotations drawn on the in-app Trend chart.
- **[hub page](README.md)** — auth, query options, refresh and troubleshooting shared by every feed.

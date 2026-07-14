# Events

The **Events** page (admins by default) keeps a shared timeline of things worth annotating
alongside your data — maintenance windows, incidents, deployments, policy changes, or anything else
that explains a blip or a step-change in the numbers. Events are purely informational: nothing in
the submission pipeline reads them, they don't affect validation, aggregation or any figure — they
exist to give context when someone (or a chart) asks "why did this change here?".

They show up in two other places once recorded:

- The [Explore page](explore.md#event-overlay)'s Trend chart draws them as vertical lines or shaded
  bands alongside the data.
- BI tools can pull them via the [`events` OData feed](../setup/powerbi/events.md) and overlay them
  on their own charts.

## Required capability

Unlike most read/manage pairs on this guide's other pages, **`events:read`/`events:manage` are not
part of the Operator's default bundle** — only **Admin** carries them out of the box. Assign
`events:manage` (which implies being able to view them too) to an Operator or a custom role via
[custom capabilities](accounts.md) if you want someone other than an admin to curate the timeline.

## Recording an event

Click **Add event** (requires `events:manage`) and fill in:

| Field | Required | Notes |
|-------|----------|-------|
| **Timestamp** | Yes | When the event occurred, or starts. |
| **Label** | Yes | Short title — this is what shows on the table row and on chart overlays. |
| **Description** | No | Longer free-text context, shown in the detail drawer. |
| **Kind** | Yes | `Point in time`, `Interval`, or `From now on` — see below. |
| **Duration** | Only for `Interval` | Disabled and cleared for the other two kinds. Accepts any of three formats — see below. |
| **Affects** | Yes (pick a scope) | **All services** (the default), or one or more specific services. |

### The three kinds

Each kind gets its own colour/icon in the events table and on chart overlays, so you can tell them
apart at a glance:

| Kind | Meaning | Duration | Avatar |
|------|---------|----------|--------|
| **Point in time** | A single instant — a deployment, a config change. | Not applicable. | Blue dot. |
| **Interval** | A bounded window with a known end — a maintenance window, an outage that's been resolved. | **Required**, in whole minutes. | Green "resize" icon. |
| **From now on** | Starts at the timestamp and runs indefinitely — a service decommissioned, a policy that took effect and hasn't changed since. | Not applicable (open-ended). | Amber arrow. |

Switching **Kind** in the form clears **Duration** unless you land on `Interval`, so you can't
accidentally save a stale duration against the wrong kind.

#### Duration formats

The **Duration** field accepts whichever shape is most natural for the length involved, and always
normalises to whole minutes underneath:

| Format | Example | Meaning |
|--------|---------|---------|
| `mmm` (minutes) | `90` | 90 minutes. |
| `HH:mm` (hours:minutes) | `1:30` | 1 hour 30 minutes (90 minutes). Hours aren't capped at 24 — `36:15` is valid (36h15m). |
| `dd HH:mm` (days hours:minutes) | `2 03:15` | 2 days, 3 hours, 15 minutes. |

Re-opening a saved event shows its duration back in the most readable of the three shapes for its
length (plain minutes under an hour, `H:mm` under a day, `d H:mm` beyond that) — not necessarily the
exact format you typed it in. An unrecognised value is rejected with an inline error before the form
can be saved.

### Affects (service scope)

Leave **All services** checked for something that's relevant registry-wide (e.g. a platform
deployment). Uncheck it to pick one or more specific services from the dropdown — useful for an
incident or maintenance window that only touched a subset of your services. This scope is what the
Explore overlay and OData feed use to decide whether an event is relevant to a given service.

## Browsing, editing and deleting

The table lists every event, newest first, with its timestamp, label (with kind avatar),
description, duration summary and affected services. Click a row to open the **view drawer**: it
shows every field plus the audit trail (created/modified, by whom). From there, the drawer's
toolbar has **Edit** and **Delete** buttons (both gated on `events:manage`) — there's no separate
read-only page; editing and deleting always happen from this same drawer or the row's **⋮** menu.

Deleting an event is a soft delete: it disappears from the list, the Explore overlay and the OData
feed, but the row (and its audit history) is preserved internally.

The page's **⋯** menu has the usual **Export this list (CSV)**, which downloads every event (not
just the current page) with its label, timestamp, kind, duration, description, affected services and
audit fields.

## Event overlay

See [Explore page → Event overlay](explore.md#event-overlay) for how events are drawn on the Trend
chart, and [PowerBI integration → Events feed](../setup/powerbi/events.md) for consuming them from a
BI tool.

## API

```
GET    /api/admin/events                # paged list, newest first
       ?page={n}&pageSize={n}
       &from={iso}&to={iso}             # optional half-open window [from, to); an event is included
                                         # when its span overlaps the window (see below)
POST   /api/admin/events                # create
PUT    /api/admin/events/{id}           # replace
DELETE /api/admin/events/{id}           # soft delete
```

`from`/`to` filter by **overlap**, not just by `timestamp`: an `Interval` event matches if any part
of `[timestamp, timestamp + duration]` falls in the window, and a `FromNowOn` event always matches
any window on/after it started (it never ends). This is the same overlap rule the Explore page and
the OData feed's `EffectiveEnd` column expose — see [PowerBI integration → Events feed § Querying by
open/closed interval](../setup/powerbi/events.md#querying-by-openclosed-interval) for the full
explanation and worked `$filter` examples.

`POST`/`PUT` bodies:

```json
{
  "timestamp": "2026-03-01T08:00:00Z",
  "label": "Database maintenance window",
  "description": "Planned failover test, no expected impact.",
  "kind": "Interval",
  "durationMinutes": 120,
  "serviceIds": []
}
```

`kind` is one of `PointInTime` / `Interval` / `FromNowOn`. `durationMinutes` is required (and must
be positive) when `kind` is `Interval` — a `400` is returned otherwise. For the other two kinds any
`durationMinutes` sent is silently cleared (never stored, never rejected). `serviceIds` empty means
"all services"; a `400` is also returned for a missing label/timestamp or a `serviceIds` entry that
isn't a real service account.

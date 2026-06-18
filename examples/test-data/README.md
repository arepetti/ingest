# Test data

Ready-made sample data for kicking the tyres of a fresh deployment — populate a few charts in
**Explore**, exercise the status dashboard, try the OData feed, without typing anything in by hand.

## `submissions.json`

Two years of weekly [`weekly_workforce`](../schemas/generic.json) submissions (104 ISO weeks,
Monday-anchored, ending mid-June 2026): active employees, sick leave, contractors and overtime
hours, with a gentle headcount trend, winter sick-leave bumps and the odd quarter-end overtime
crunch.

### How to load it

Bulk import assigns the **service at import time** (it isn't in the file), so import the same file
once per service you want populated:

1. Upload the schema if it isn't already there: **Schemas → New schema → Upload JSON…** with
   [`examples/schemas/generic.json`](../schemas/generic.json) (it's global, so every service can use
   it).
2. Make sure the target services exist — for the intended demo: `finance`, `garbage_collection`
   and `it`.
3. **Submissions → Import**, pick a service, choose this file, import. Repeat for each service.

All three services share the same baseline numbers; tweak the file (or re-import to a subset) if you
want them to diverge for a more interesting **Compare services** view.

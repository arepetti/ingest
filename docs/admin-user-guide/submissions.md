# Submissions

Open **Submissions** in the sidebar.

## Browsing

The grid lists every submission across every service, newest first. Filters at the top:

- **Service** — narrow to a specific account.
- **Date range** — preset (*last week*, *last month*, *last year*) or *custom* with two date pickers.

Clicking a row opens a read-only drawer showing the service label, the schema, audit info, and a two-column table of `(value name → submitted value with unit)`.

## Creating a submission on behalf of a service

Use this to back-fill data, fix mistakes, or test a new schema.

1. Click **New submission**.
2. Pick the **service** from the dropdown (only Service-role accounts).
3. Pick the **schema** to submit against (only those visible to the selected service).
4. The form expands with one row per **value** in the schema:
   - Required values are starred; you cannot save with any required value empty.
   - Optional values can be left blank — they're simply not submitted.
   - Each value's unit is displayed inline.
   - Values whose `Visible if` rule evaluates to false (given the rest of the form) are **hidden**; values whose `Enabled if` is false are rendered **greyed out**. The submission editor evaluates these rules live, so changing one input can show or hide another.
   - When a `Warning` rule fires for a value, an italic note appears under it explaining what the warning says.
5. Optional: click **Add notes** to expand a free-form notes field.
6. Click **Save**. The audit trail records you (the admin) as the creator; the submission is otherwise indistinguishable from one the service would have posted itself.

If the server emits warnings on accept (typically because a `Warning` rule fired, or a `Visible if` / `Enabled if` was false on a value you submitted), the editor stays on the page and shows the warnings in a banner instead of navigating away. Inspect them; they're not blocking, the submission is already saved.

## Editing a submission

Row menu → **Edit**. Same form as create, pre-populated. Admin edits ignore the cadence-window restriction entirely — you can rewrite a submission from two years ago. The `ModifiedBy` audit field records your identity.

## Deleting a submission

Row menu → **Delete**. Soft-delete only — the database row is retained for audit, but downstream queries (OData, `/api/admin/query`) stop seeing it.

## Where to go next

- [schemas.md](schemas.md) — the schema editor is where the rules you see fire in the submission editor are authored.
- [validation.md](validation.md) — when a rule rejects a value or fires a warning, this page explains how it was written and how to interpret it.
- [../client/api.md § `POST /api/submissions`](../client/api.md#post-apisubmissions) — the API a service calls to do the same thing programmatically.

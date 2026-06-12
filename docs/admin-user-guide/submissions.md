# Submissions

Open **Submissions** in the sidebar.

## Browsing

The grid lists every submission across every service, newest first. Filters at the top:

- **Service** — narrow to a specific account.
- **Date range** — preset (*last week*, *last month*, *last year*) or *custom* with two date pickers.

The **Warnings** column shows a count badge when a submission carries non-blocking warnings (and a dash when it has none). Warnings are recorded at the last write, so the count reflects the current stored state of the submission. Submissions created before warnings were stored show no count.

Clicking a row opens a read-only drawer showing the service label, the schema, audit info, the list of any warnings, and a two-column table of `(value name → submitted value with unit)`. The full **View details** page also surfaces the same warnings.

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

## Bulk importing historical submissions (admins only)

When you have a lot of history to load for one service — months of past readings, a migration from another system — use **Import** (top toolbar, admins only) instead of entering each submission by hand. It accepts a single **JSON** or **CSV** file containing many submissions, all attributed to one service you choose.

1. Click **Import**.
2. Pick the target **service** (every submission in the file is attributed to it).
3. Choose the **file**. The **format** is auto-detected from the extension (`.json` / `.csv`) — override it if your file uses a different extension.
4. Click **Import**. The result panel reports, per submission, whether it was imported (with any warnings) or rejected (with the errors that caused it).

Each imported submission goes through exactly the same validation as one created through the form, and the audit trail records you (the admin) as the creator.

> **Not a database restore.** This is for back-filling submission *data* for one service, not for backing up or migrating the whole registry. For that, see [hosting.md](../setup/hosting.md).

### How importing behaves

- **Parsing is all-or-nothing.** If the file can't be parsed — invalid JSON, a missing CSV column, an unparseable timestamp — nothing is imported and the errors list exactly what's wrong. Fix the file and re-upload.
- **Importing is not transactional.** Once the file parses, each submission is validated and saved on its own. A submission that fails validation is reported as failed and skipped; the rest still import. Because of this, re-uploading a file re-imports the submissions that already succeeded (creating duplicates) — so after a partial import, trim the file down to just the failed groups and upload that.

### JSON format

Either an array of submissions, or an object with a `submissions` array. Each submission has a `samples` array; each sample mirrors the `POST /api/submissions` body. A single-submission file may also be just `{ "samples": [ … ] }`.

```json
{
  "submissions": [
    {
      "samples": [
        { "schemaName": "roads", "valueName": "length_km", "value": 1234.5, "timestamp": "2024-01-31T00:00:00Z" },
        { "schemaName": "roads", "valueName": "resurfaced", "value": true,   "timestamp": "2024-01-31T00:00:00Z" }
      ]
    },
    {
      "samples": [
        { "schemaName": "roads", "valueName": "length_km", "value": 1240.0, "timestamp": "2024-02-29T00:00:00Z" }
      ]
    }
  ]
}
```

### CSV format

One sample per row. A header row is required; columns (case-insensitive) are **`schemaName`, `valueName`, `value`, `timestamp`** (required) plus optional **`group`** and **`note`**. Rows that share the same `group` value form one submission, in first-seen order; if there is no `group` column the whole file is a single submission.

```csv
group,schemaName,valueName,value,timestamp,note
2024-01,roads,length_km,1234.5,2024-01-31T00:00:00Z,
2024-01,roads,resurfaced,true,2024-01-31T00:00:00Z,"annual programme"
2024-02,roads,length_km,1240.0,2024-02-29T00:00:00Z,
```

Notes on CSV values:

- **Timestamps** are parsed as UTC; use ISO 8601 (e.g. `2024-01-31T00:00:00Z`).
- **Booleans** must be `true` / `false`. Every other value is taken as text and then coerced to the value's declared type (number, integer, date, …) by the schema — so a numeric column "just works" against a numeric value.
- An **empty** `value` cell means "no value submitted" (the same as omitting an optional value).
- Use standard CSV quoting (`"…"`) for values that contain commas, quotes, or line breaks.

## Editing a submission

Row menu → **Edit**. Same form as create, pre-populated. Admin edits ignore the cadence-window restriction entirely — you can rewrite a submission from two years ago. The `ModifiedBy` audit field records your identity.

## Deleting a submission

Row menu → **Delete**. Soft-delete only — the database row is retained for audit, but downstream queries (OData, `/api/admin/query`) stop seeing it.

## Where to go next

- [schemas.md](schemas.md) — the schema editor is where the rules you see fire in the submission editor are authored.
- [validation.md](validation.md) — when a rule rejects a value or fires a warning, this page explains how it was written and how to interpret it.
- [../client/api.md § `POST /api/submissions`](../client/api.md#post-apisubmissions) — the API a service calls to do the same thing programmatically.

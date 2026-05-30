# Reports

The reporting feature lets admins upload HTML files that get rendered against either a **single submission** or an **aggregated period of submissions** for one of your schemas. Operators (and admins) can then view the rendered HTML in the admin SPA and tweak the period to re-render. There is no in-app editor — reports are content you author externally, drop into the catalogue, and re-upload if you want to change them.

Use them for:

- One-page summaries of a specific submission (think "what did service X submit today?").
- Period roll-ups across services for a single schema (think "this month's tonnage across every collection round").
- Lightweight printable views you can hand to a non-technical colleague who doesn't have an account.

If you want serious analytics, point PowerBI at the OData feed (see [setup/powerbi.md](../setup/powerbi.md)). Reports are deliberately simpler — text, tables and a couple of summary cards rendered against curated server-side data.

## Authoring a report

A report is a plain UTF-8 file (`.html` is the convention). It has two parts:

1. A small **YAML front matter** block between two `---` fences at the top. This is metadata: name, label, description, type, target schemas.
2. The **Liquid template body** below the closing fence. This is what gets rendered.

Minimal example:

```html
---
name: tonnes_summary
label: "Tonnes summary"
type: Aggregate
schemas: [garbage_collection]
---
<h1>{{ schema.label }}</h1>
<p>Window: {{ range.from }} → {{ range.to }} · {{ totals.sampleCount }} samples.</p>
<ul>
{% for v in values %}
  <li>{{ v.label }}: {% for b in v.buckets %}{{ b.sum }}{% if forloop.last == false %}, {% endif %}{% endfor %}</li>
{% endfor %}
</ul>
```

### Front matter

| Field         | Type          | Required | Notes |
|---------------|---------------|----------|-------|
| `name`        | string        | one of   | Machine-style id (URL segment). Required *or* the file name is used to derive one. |
| `label`       | string        | no       | Friendly title. Defaults to the name. |
| `description` | string        | no       | Free-form description shown next to the title in the catalogue and at the top of the viewer. |
| `type`        | `Single` / `Aggregate` | no | Defaults to `Aggregate`. Drives the data envelope handed to the template. |
| `schemas`     | list of strings | no     | Schemas the report targets, by machine-style name. Empty / omitted = **global** (any schema). |

The `schemas:` list accepts both inline (`[a, b]`) and block (`- a` / `- b`) forms. Quoted scalars are accepted and quotes are stripped.

Unknown keys are ignored, so you can decorate your files with extra notes for yourself without breaking the parser.

### Templates

The body is a [Liquid](https://shopify.github.io/liquid/) template. It runs in a sandboxed engine — there is **no** access to .NET reflection, file I/O, or the network. Only the data envelope described below is in scope.

The engine is [Fluid](https://github.com/sebastienros/fluid), so all the standard Liquid filters (`default`, `date`, `escape`, `size`, `upcase`, …) and control-flow tags (`if`, `for`, `case`, …) work. Reports render best when you keep the markup self-contained: inline styles, no external scripts, no images that need authentication. The viewer drops the rendered HTML into a `sandbox=""` iframe so external resources, scripts and forms are blocked anyway.

### Single-type data envelope

The template gets:

```
report      → { id, name, label, description, type, targetSchemaNames }
range       → { from, to }                  # the period filter the user picked
schema      → { id, name, label, description, version, values: [...] }  # null for global reports
service     → { id, name, label }           # the submission's owning service
submission  → {
                id, serviceAccountId, serviceName, submittedAt, replacedAt,
                createdAt, createdBy, modifiedAt, modifiedBy,
                samples: [
                  { schemaName, valueName, label, unit, type, value, timestamp, note }, ...
                ]
              }
```

`submission.samples` is the flat list of every sample in the picked submission. When the report is scoped to a schema, samples for other schemas are filtered out and `label` / `unit` / `type` are joined in from the schema definition.

### Aggregate-type data envelope

```
report → { id, name, label, description, type, targetSchemaNames }
range  → { from, to }                    # period the user picked
schema → { id, name, label, description, version, values: [...] }
services → [ { id, name, sampleCount } ]  # one per service with activity in the window
values   → [
  {
    name, label, type, unit, cadence, description, required,
    buckets: [
      { valueName, periodStart, periodEnd, min, max, average, sum, count,
        services: [ { id, name } ] }, ...
    ],
    samples: [
      { serviceAccountId, serviceName, timestamp, periodStart, periodEnd,
        value, numeric, note }, ...
    ],
  }, ...
]
totals → { sampleCount, serviceCount }
```

`buckets` follows the per-value cadence (Daily, Weekly, Monthly, …); each bucket is the aggregation of every sample for that value falling in that cadence window. `samples` is the flat list of all samples for the same value in the period, handy for "one row per submission" tables. Numeric values populate `min`/`max`/`average`/`sum`; non-numeric values still get `count`.

### Filters & defaults

The viewer always passes:

- `from` / `to` — the period range. Defaults to the start of the current calendar month → now.
- `schemaName` — required when the report is global or has more than one target. Skipped when the report targets exactly one schema (the only candidate wins).
- `submissionId` — Single only. Required; the picker lists submissions whose timestamps fall inside `[from, to)` and that contain samples for the chosen schema.

A `400` is returned when a required filter is missing. `404` for an unknown report / schema / submission. Template parse or runtime errors also surface as `400` so the SPA can show the message inline.

## Uploading a report

1. Sign in as an **Admin**.
2. Open **Reports** in the sidebar.
3. Click **Upload report**, pick the `.html` file. The server parses the front matter, stores the parsed metadata alongside the original document text, and the new report appears in the list immediately.

Re-uploading an existing name returns a 409 conflict — to replace a report, delete the old one first (rows have a **Delete** action in the dropdown menu).

The upload endpoint also has a JSON variant (`POST /api/admin/reports/json` with `{ fileName, content }`) for non-browser tooling.

## Viewing a report

Operators and admins both see the **Reports** page in the sidebar. Click a row to open the viewer:

- The filter bar at the top lets you pick the **schema** (when the report targets more than one), the **period** (last week / last month / last year / **custom**) and — for `Single` reports — a specific **submission**.
- The **Render** button posts the filters to the server, which renders the template and returns the HTML.
- The result is shown in a sandboxed iframe. Use **Expand** to fill the screen, **Open in new tab** for a standalone window (handy for printing).

The server-side render is stateless — every click re-runs the template against fresh data, so there is no cache to invalidate.

## Sample reports

Four examples live under `/samples/reports/`:

| File | Type | Targets | What it does |
|------|------|---------|--------------|
| `single_submission_table.html`           | Single    | global                                                       | Renders any submission as a plain table. Useful for ad-hoc review. |
| `garbage_collection_daily_summary.html`  | Single    | `garbage_collection`                                         | One-page summary card with headline KPIs + a per-value table. |
| `workforce_weekly_aggregate.html`        | Aggregate | `weekly_workforce`                                           | Min/avg/max/sum/count per cadence bucket, across services. |
| `multi_schema_aggregate.html`            | Aggregate | `garbage_collection`, `finance_monthly_close`, `weekly_workforce` | Flat per-service samples list — the viewer picks the schema. |

Drop one into the **Upload report** picker to try it. All four are deliberately styled with inline CSS so they look reasonable in the sandboxed iframe without any external assets.

## API

See [client/api.md](../client/api.md#reports) for the full request/response shape if you want to render reports from your own tooling rather than through the SPA.

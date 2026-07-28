# Schemas

Open **Schemas** in the sidebar. Each schema is a **package** of related KPI values that a service reports together — for example a "monthly KPIs" schema with `tonnes_collected`, `incidents`, and `downtime_hours`.

## Anatomy of a schema

```
Schema (package)
├─ Name, Label, Description, Notes
├─ Modifiable, Enabled            ← package-level gates
├─ Audience (global | restricted) ← who sees it
├─ Version (integer, monotonic)   ← bumped when adding new values; drives the "New" tag
├─ Schema-level validations[]     ← cross-value rules, one per row
├─ Layout[] (optional)            ← UI-only section/subsection grouping
└─ Values[]
   └─ SchemaValue (single KPI)
      ├─ Name, Label, Description, Notes
      ├─ Caption (optional, UI-only)
      ├─ Type (String / Integer / Number / Date / Boolean)
      ├─ Unit
      ├─ Cadence (Daily / Weekly / Fortnightly / Monthly / Quarterly / Semi-annually / Yearly)
      ├─ Required, Modifiable, Enabled  ← per-value gates
      ├─ SinceVersion (optional)        ← schema version the value was introduced in
      ├─ Min / Max / MinDate / MaxDate / MinLength / MaxLength / RegexPattern
      ├─ RAG band (optional, numeric)  ← GreenMin/Max + AmberMin/Max; shown on charts; never enforced
      └─ Expression fields           ← Value validation, Warning, Enabled if, Visible if
```

Each value has its own cadence: a monthly schema can contain weekly KPIs perfectly fine. The cadence is what the validator uses to enforce "only one submission per period" and what `/api/me/status` rolls up against.

## Creating a schema

1. Click **New schema**.
2. Fill in the package-level fields:
   - **Name** — stable machine-style identifier (e.g. `monthly_kpis`). Must be globally unique.
   - **Label** / **Description** / **Notes** — friendly fields used by the UI and reports.
   - **Modifiable** — when off, samples already submitted against this schema cannot be replaced (even by Admins — see *Per-value modifiability* below).
   - **Enabled (Published)** — the schema's publication state. **Enabled = Published**: services can submit against it. **Enabled off = Draft**: submissions are rejected. A freshly created schema is Published if you leave the box ticked. Throughout the app and this guide, "Published" and "Draft" are just friendly names for this single flag.
   - **Audience** — *Global* (every service sees it) or *Restricted to specific services* (you pick from a dropdown of Service-role accounts).
   - **Schema-level validations** — a list editor where each row is one validation rule. Add as many as you like; each runs once per submission with every value of the schema in scope. Use these for cross-value checks like `revenue >= expenses` or `total == a + b`. See [validation.md § Schema-level rules](validation.md#schema-level-rules-cross-value) for the full syntax.
   - **Approval** — (only when the [approval workflow](approval-process.md) is enabled) whether submissions for this schema need review before going live. Choose *no approval*, *use the global default*, or *approval required* with a source scope (manual / API / both) and a set of approvers (specific accounts holding `submissions:approve` and/or the **service owner**, i.e. the account that sent the submission). The editor warns if a **modifiable** schema requires approval, because re-submitting a window resets approval and can drop previously-approved data out of reporting. See [approval-process.md](approval-process.md). Schemas that are gated by approval — whether by their own policy or by deferring to a global default that requires it — show a small **shield-checkmark** next to their name in the list and a **Requires approval** badge in the preview drawer.
3. Add at least one **value** (the actual KPIs). For each value:
   - **Name** / **Label** / **Description** / **Notes**.
   - **Caption** — optional heading rendered above this value when filling in or viewing a submission (think section title). Use it to group related inputs visually — *"Collection metrics"*, *"Financial"*, *"Equipment"*. Display-only: it doesn't affect validation and API callers never see it as anything other than another field on the schema definition.
   - **Type** — `String`, `Integer`, `Number`, `Date`, or `Boolean`.
   - **Unit** — free-form (e.g. `t`, `hours`, `%`). Displayed alongside values in the UI and PowerBI.
   - **Cadence** — `Daily`, `Weekly`, `Fortnightly` (every 2 weeks), `Monthly`, `Quarterly`, `Semi-annually` (every 6 months) or `Yearly`. The validator uses this to enforce "one sample per period". Fortnightly windows are Monday-anchored and aligned to a fixed reference so every service sees the same biweek boundaries; quarterly maps to calendar quarters (Q1 = Jan–Mar, Q2 = Apr–Jun, Q3 = Jul–Sep, Q4 = Oct–Dec); semi-annual maps to H1 (Jan–Jun) and H2 (Jul–Dec).
   - **Required** / **Modifiable** / **Enabled** — per-value flags. `Modifiable` and `Enabled` AND with the package-level flags.
   - **Min / Max / MinDate / MaxDate / MinLength / MaxLength / RegexPattern** — type-specific shape constraints. Only the ones that make sense for the chosen type take effect.
   - **RAG target band** — *(Integer / Number only)* an optional Red/Amber/Green band describing where a KPI *should* sit. See [The RAG target band](#the-rag-target-band) below. Unlike Min/Max it is **never enforced** — out-of-band samples are still accepted — it's purely reporting metadata drawn on the [Explore](explore.md) and historical charts.
   - Four expression fields (described in [Expression fields on a value](#expression-fields-on-a-value) below).
4. Click **Save**.

## Expression fields on a value

Each schema value has four optional expression fields. They run at different points and answer different questions:

| Field            | When it runs                                            | What a falsy/true/string result does                                                                                              |
|------------------|---------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------|
| **Value validation** | Once per submitted sample of this value.            | `true` / `null` / `''` → accept. `false` → reject with a generic message. Non-empty string → reject and use the string verbatim. |
| **Warning**          | Once per submitted sample of this value.            | `false` / `null` / `''` → silent. `true` → emit a default warning. Non-empty string → emit it as the warning.                    |
| **Enabled if**       | Before any other check, against the whole submission. | When **false**, the input is greyed out in the UI; the sample is dropped server-side and a warning is added to the response.     |
| **Visible if**       | Before any other check, against the whole submission. | When **false**, the input is hidden in the UI; the sample is dropped server-side and a warning is added to the response.         |

A few authoring rules of thumb:

- **Use Value validation for hard rejections** that should be reported as errors (`if(value < 0, 'cannot be negative', null)`).
- **Use Warning for soft thresholds** — anything where you want the operator to notice but the data is still acceptable (`if(value > 200, 'unusually high', null)`).
- **Use Enabled if / Visible if for conditional fields** — when a value only makes sense given the rest of the submission (e.g. `incident_notes` is only relevant when `incident_count > 0`). Required values whose conditional rule is false are exempt from the required check.

`Visible if` and `Enabled if` are *server-equivalent* — they both discard the sample with a warning. The difference is purely cosmetic in the UI: hide vs grey out. Pick whichever feels right.

Any warnings these rules produce are **stored on the submission**, not just shown once at submit time. They appear as a count in the Submissions grid and as a list in the submission view (see [submissions.md](submissions.md)), so operators and admins can review them whenever they revisit a record.

The full syntax, operators, helpers and recipes are in [validation.md](validation.md). The submission editor evaluates the same expressions live so admins can see hide/grey/warning behaviour as they type test data.

## The RAG target band

Numeric values (`Integer` / `Number`) can carry an optional **Red/Amber/Green band** that says where the KPI *should* sit. It is **never enforced** — a sample outside the band is accepted exactly as before — it's purely reporting metadata, drawn as shaded zones behind the line on the [Explore](explore.md) trend chart and the [historical-data view](#viewing-historical-data). It gives a leadership audience an at-a-glance read on whether a service is on target.

The band is two **nested ranges** defined by four optional numbers:

```
   RED   │   AMBER   │       GREEN       │   AMBER   │   RED
─────────┼───────────┼───────────────────┼───────────┼─────────▶
      AmberMin     GreenMin          GreenMax     AmberMax
   └──────────────  Acceptable range  ──────────────┘
                └──────  Ideal range  ──────┘
```

- **Acceptable (amber) range** — `Acceptable min` … `Acceptable max`. Anything outside it is **red**.
- **Ideal (green) range** — `Ideal min` … `Ideal max`, sitting inside the acceptable range. Inside it is **green**; inside the acceptable range but outside the ideal range is **amber**.

Every edge is optional, so you can describe exactly the shape you need:

- **Acceptable range only** — a simple in-range (amber) vs out-of-range (red) band, with no finer "ideal" distinction.
- **One-sided** — e.g. set only `Ideal max` + `Acceptable max` for a "lower is better" KPI (no minimums), or only the minimums for "higher is better".
- **Full four-edge band** — the complete green-inside-amber picture.

The server validates that the band is **coherent** (and rejects the schema otherwise):

- Edges read low-to-high: `Acceptable min ≤ Ideal min ≤ Ideal max ≤ Acceptable max` (for whichever are set).
- The ideal range needs a surrounding acceptable range **on the same side**: `Ideal min` requires `Acceptable min`, and `Ideal max` requires `Acceptable max`. (You can have an acceptable range without an ideal one, but not the other way round.)

On the charts a missing outer edge means "no red on that side" — the band simply extends to the edge of the chart.

## Multi-line expression authoring

Every expression field is a **textarea**: long rules can be broken across multiple lines for readability, with indentation to mirror the structure. The editor preserves your formatting verbatim and the engine normalises whitespace before evaluation — newlines and runs of spaces collapse to a single space.

So a long `if(...)` chain can be authored like this and read like English:

```text
if(
    expenses > revenue,
    'expenses cannot exceed revenue (' + expenses + ' > ' + revenue + ')',
    null
)
```

Or like this:

```text
if(value < 0,    'cannot be negative',
if(value > 100,  'cannot exceed 100',
null))
```

Use it freely — multi-line authoring is the recommended style for anything with more than one operator. See [validation.md § Whitespace and line breaks](validation.md#whitespace-and-line-breaks) for the full guarantees.

The editor checks each rule's **syntax** as you type (a green "Valid syntax" hint appears under the field, or a red "Syntax error: …" line when the parser stumbles). Unknown identifiers or function names are *not* flagged here — full validation happens server-side when you click **Save**.

## Organising values into sections

For schemas with more than a handful of values, dragging values into **sections** keeps the submission form readable. Sections only affect how the editor and view drawers lay out the inputs — submissions still travel the wire as a flat list of samples, so client integrations are unaffected.

In the schema editor, scroll to **Layout (UI grouping)** below the values list:

- The **Unassigned values** tray at the top lists every value that hasn't been placed yet. Drag a chip into the tree to insert it.
- The tree underneath mirrors the schema's layout. Add a new section with **Add section** at the top (root section) or with the inline **+** next to an existing section header (subsection). Nesting is unbounded.
- Each section has a **Caption** (the heading rendered in the submission form) and an optional **Description** (a sub-heading shown directly below). Captions are required.
- Drag a node by its grip handle to reorder, move it across sections, or move it back to the tray.
- Empty sections **never appear** in the submission form or view drawer — if every descendant is hidden by `Visible if` (or simply missing from a submission), the section vanishes with them.
- Values left in the tray still render in the submission form, but they appear first under no heading.

## Previewing a schema

Before you save, the **Preview** button (in the editor toolbar and at the bottom of the form) opens a full-screen window that renders the submission form for the schema **exactly as you have it right now** — unsaved. Type values into it to see how the schema behaves:

- **Conditional display.** Values whose `Visible if` evaluates false disappear; values whose `Enabled if` evaluates false render greyed-out with a "disabled" badge. Sections whose every value is hidden fold away, just like the real form.
- **Warnings.** A value's `Warning` rule shows its message inline under the value as soon as it fires.
- **Validation results.** A side panel lists, grouped by kind: missing **required** values, basic **shape** problems (min/max, lengths, regex, type), failed per-value **Value validation** rules, and failed **schema-level** rules — recomputed as you type.
- A **Sample timestamp** picker seeds the samples and any date-based rules that read a `Date` value, and **Reset values** clears the form. Nothing here is saved.

Values with a blank or duplicate name can't be referenced by rules, so the preview skips them and tells you which — give every value a unique name to preview it. Preview also works in the read-only schema view and version snapshots, so you can poke at a published or historical schema without touching it.

> [!IMPORTANT]
> **The preview is a best-effort, client-side approximation — the server is always authoritative.** Rules are translated and evaluated in your browser, so some behaviour can differ: regular expressions use the browser's engine (not .NET's), and submission helpers such as `sampleTimestamp()`, `sampleNote()`, and `serviceName()` aren't available client-side (they evaluate to empty). Treat a green preview as encouraging, not a guarantee — confirm any rule you rely on with a real submission or an API call before publishing.

### Validating on the server

For an authoritative check, the **Test submission** row action on the schemas list (next to **Edit**) opens the same form and runs the *real* server validation without saving anything. Because it validates the **stored** schema, it's only available for **saved** schemas — not the unsaved editor draft. Some checks depend on context the browser doesn't have, so the dialog asks for two things, shown at the top:

- **Validate as service** — the Service-role account to validate against. This determines which schemas are visible, what the service's submission history is (so `latest()` / `previous()` rules and the one-per-period cadence check have real data), and which approval policy applies.
- **Sample timestamp** — the instant every sample is stamped with, which decides the cadence window and anchors any date/history rules.

You can also tick **Skip cadence (one-per-period) checks** to ignore whether a value has already been submitted for the period — handy when you only want to confirm the shape and rules. Fill in the form, then press **Validate**: the result shows whether a real submission would be **accepted**, lists the server's errors and warnings, notes any conditionally-discarded values, and previews the **would-be approval state** (accepted immediately, or held for approval). The schema must be assigned to the chosen service for this to be meaningful — it validates the stored schema as that service would see it.

## Visualizing value dependencies

The **Dependencies** button (editor toolbar, next to **Preview**) opens a diagram of how this schema's values reference one another through their rules — handy for spotting an unexpected chain before you save, or just understanding an unfamiliar schema at a glance. Every value is a node arranged in a circle; a connector is drawn for each rule that references another value:

| Connector | Rule | Points from → to |
|-----------|------|-------------------|
| Solid blue | `Calculated` value's expression | the values it reads → the calculated value |
| Dashed teal | `Visible if` | the referenced value → the value it shows/hides |
| Dash-dot purple | `Enabled if` | the referenced value → the value it enables/disables |
| Long-dashed red | `Value validation` | the referenced value → the value being validated |
| Fine-dotted amber | `Warning` | the referenced value → the value carrying the warning |
| Dotted grey (no arrowhead) | a schema-level **cross-value validation** | undirected — chains together every value the rule mentions |

Calculated values are drawn with a dashed border and tinted background so they stand out from ordinary values; every other value looks the same regardless of which rules it carries. Hover any connector to see the exact expression driving it. A rule referencing itself (e.g. a `Warning` that reads its own value) never draws a self-loop — there's nothing to relate it to.

Like Preview, this works from the **unsaved** editor draft (and in the read-only schema view and version snapshots): opening the dialog sends every rule on the schema to the server, which parses each one with the same expression engine used to enforce the rules at submission time and reports back exactly which values it references — so the diagram reflects a real dependency walk, not a guess. Nothing here is enforced by the diagram itself, though — it's a picture of what the rule editors and server validation already do.

## Schema versioning

Every schema carries an integer **Version**. New schemas start at `1`. When you introduce a new value to an existing schema:

1. Bump **Version** by one.
2. Set the new value's **Since version** to the new schema version.

The submission form will then render a small **New** badge next to the value's label. The badge auto-expires after **one cadence period** of the value (so a daily-cadence value sheds the badge after a day, a yearly one after a year). The clock starts from the moment the schema's version was bumped — the server stamps it (`versionModifiedAt`) on every version change and on clones. Older schemas with an unset `versionModifiedAt` never show the badge.

Rules:

- Version is **monotonic**: the server rejects updates that would lower it.
- **Since version** must be in `[0, Version]`. Leave it empty for values that have always been part of the schema.
- Cloning a schema (see below) resets `versionModifiedAt` to "now" so the clone gets a fresh window.

### The publish prompt

You aren't *forced* to bump the version on every edit. But changing a **Published** schema without bumping the version can silently shift what services see, so the editor nudges you. When you save and **all** of these are true — the schema is **Published** (Enabled), you made changes, and you left the version number unchanged — a dialog appears asking what to do:

| Option | What it does |
|--------|--------------|
| **Automatically increment the version number** | Bumps the version by one, then saves. The recommended choice when you added or changed values. |
| **Publish as-is without changing the version** | Saves your changes against the same version number. Use for cosmetic edits (labels, descriptions). |
| **Move the schema back to Draft and apply the changes** | Unpublishes the schema (Enabled → off) and saves. **Only available when no submissions exist yet** — once data has arrived against the schema, you can't quietly pull it back to Draft. |
| **Discard the changes** | Throws your edits away and returns to the schema list. |
| **Cancel and keep editing** | Closes the dialog and leaves you in the editor with your changes intact. |

The prompt is purely a guard rail in the UI — it never appears for **Draft** schemas, for brand-new schemas, or when you've already changed the version yourself.

## Description info icon

When a value has a non-empty **Description**, the submission editor and view drawer render a small **(i)** icon next to the value's label. Hover the icon to see the description in a tooltip. Use it for short explanatory text — "Tonnes collected at the gate before sorting" — that you don't want competing with the input for vertical space.

## Drawer expansion

Every drawer (schema editor, schema view, submission editor, submission view, account editor, account view) has an **expand** icon in the top-right corner, just before the close button. Click it to widen the drawer to the full viewport — handy when editing a big schema with many values or reviewing a dense submission. Clicking again (or closing the drawer) restores the default width.

## Importing, exporting and cloning

The schema list and the schema view drawer expose three quick affordances:

| Where | Action | Result |
|-------|--------|--------|
| Schemas page header → split button on **New schema** → **Upload JSON…** | Open a JSON file and load it into the editor (you can review and tweak before saving). | Accepts the same JSON shape this app emits on download — useful for moving schemas between environments. |
| Schema view drawer → split button on **Download** → primary action | Download the schema as JSON (`{name}.schema.json`). | A clean export with audit fields stripped — drop it in source control or feed it back into the upload affordance. |
| Schema view drawer → split button on **Download** → **Example submission (JSON)** | Download a starter submission body (`{name}.example.json`). | One sample per value with type-appropriate defaults (empty string, `0`/`Min` for numerics, today/`MinDate` for dates, `false` for booleans). Validation rules are ignored — it's a template, not a guaranteed-valid payload. |
| Schemas page row menu → **Clone** | Server-side clone with a fresh unique name. | The new schema keeps every field (values, layout, version, audience, rules) but gets a unique name (`{source}_copy`, then `{source}_copy_2`, …) and its audit/`versionModifiedAt` reset to now. |

### Example schemas to start from

The repository ships a few ready-made schema definitions under [`examples/schemas/`](../../examples/schemas/) that you can feed straight into **Upload JSON…** and tweak:

| File | Schema | What it covers |
|------|--------|----------------|
| [`examples/schemas/garbage-collection.json`](../../examples/schemas/garbage-collection.json) | `garbage_collection` | Daily kerbside-collection operations (tonnage, routes, fleet, recycling) with mixed cadences and conditional fields. |
| [`examples/schemas/generic.json`](../../examples/schemas/generic.json) | `weekly_workforce` | A lightweight weekly headcount/availability snapshot any department can use as a starter. |
| [`examples/schemas/finance-monthly-close.json`](../../examples/schemas/finance-monthly-close.json) | `finance_monthly_close` | A monthly finance close with budget/variance, revenue, and reconciliation checks. |

They're also the schemas the [example integrations](../../examples/integrations/README.md) and [example reports](../../examples/reports/README.md) are built against, so uploading them lets you try the whole flow end to end.

## Editing a schema

Row menu → **Edit**. You can add/remove values freely; existing submissions retain their original shape thanks to the denormalised sample projection.

A few subtleties:

- Disabling a value (`Enabled = false`) immediately starts rejecting submissions against it. The value is still listed in `/me/status` with `enabled: false` so consumers can render a complete UI.
- Marking a value `Modifiable = false` prevents `PUT /api/submissions/{id}` from changing a previously-accepted sample. Service callers see a 400; admins still get through via the admin endpoints.
- Marking the **schema** `Modifiable = false` has the same effect across every value.

## Disabling vs deleting

- **Disable / move to Draft** (uncheck *Enabled (Published)* on the schema) — the schema row stays and incoming submissions against it are rejected, but every existing sample remains visible to status, the OData feed, charts, and reports. Reversible: re-tick *Enabled* to Publish it again.
- **Delete** (row menu → *Delete*) — soft-delete. The schema disappears from the default listing and no further data can be submitted against it. Recovery requires manual database surgery.

> The server refuses to delete a schema that has any live submission referencing it (HTTP 409, "Schema '…' is referenced by one or more submissions and cannot be deleted. Disable it instead…"). This protects historical samples from becoming orphaned. If you really want the schema gone, hard-delete the submissions first; otherwise just disable it.

> If you delete a schema and later create a new one with the same name, the tombstone is replaced automatically — the create succeeds rather than failing with a "schema already exists" conflict. The same applies to renaming an existing schema onto a tombstone's name. Any old samples that used to reference the deleted schema were already soft-deleted along with their parent submissions (a prerequisite for deleting the schema in the first place) and stay excluded from cadence checks, status, reports, and the OData feed.

## Per-value modifiability and the cadence window

Service-role callers face two stacked restrictions when trying to update a sample:

1. The sample's cadence window must still be **open**. Once the next bucket starts, the sample is immutable to that service.
2. Each affected value must still be **modifiable**.

Admins bypass restriction (1) via the on-behalf-of submission endpoints. Restriction (2) holds even for admins.

## Viewing historical data

Row menu → **View historical data** opens a per-value time series chart, one chart per numeric value in the schema, with min/max/average per cadence bucket. Useful for sanity-checking that data has been arriving steadily. Any value with a [RAG target band](#the-rag-target-band) shows it as green/amber shaded zones behind its chart.

In the schema view drawer the same affordance is a **split button**: the primary action still opens the historical-data charts, and the chevron menu adds **View version history** (also available from the row menu).

## Viewing version history

Every time a schema is saved — on first creation and on each later edit — the server records a **snapshot** of the whole schema in a separate history log. This is independent of the live schema: browsing, exporting, or deleting history **never changes the current schema**.

Open it from the row menu → **View version history**, or the **View historical data** split button → **View version history**. The page is a standard data table with the usual three-dots menu (Refresh, Export CSV) and a period filter. Columns:

| Column | Meaning |
|--------|---------|
| **Change date** | When the save happened. |
| **Author** | Who saved it (blank for changes made outside an authenticated session). |
| **Old version** | The version before the save (blank for the initial create). |
| **New version** | The version after the save. A **bumped** badge marks saves that changed the number. |
| **Status** | Whether the schema was **Published** or **Draft** at that point. |
| **Submissions** | How many submissions existed for the schema at the time of the save. |

Click a row (or row menu → **View this version**) to open a **read-only** copy of the editor showing the schema exactly as it was at that point in time. Nothing on that page can be changed — it's purely for inspection.

### Cleaning up history (requires `schemas:manage`)

Accounts that can manage schemas can prune the history to reclaim space or cut noise:

- **Row menu → Delete this entry** removes a single snapshot.
- **Three-dots menu → Delete all history** clears every snapshot for the schema (after a confirmation).

Both actions are **recorded in the audit log** (as a Delete against the schema), so cleanup is itself traceable. As above, deleting history has no effect on the live schema or its current version.

## Where to go next

- [validation.md](validation.md) — the rule-authoring reference. Read this when you want anything more than `value >= 0`.
- [submissions.md](submissions.md) — fill in test data through the on-behalf-of form to exercise your rules end-to-end.
- [../client/api.md § `POST /api/submissions`](../client/api.md#post-apisubmissions) — what a service sees when a rule rejects them or fires a warning.

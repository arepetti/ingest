# Writing validation rules

This guide is for **schema authors and admins** who want submitted data to be checked before it lands in the database. It is not about the type/min/max boxes in the schema editor (those are obvious) — it's about the free-form **rule** fields where you can write your own conditions:

- the **value-level rule** that runs once per submitted sample,
- the **schema-level rule** that runs once per schema and can compare values across the submission,
- the **conditional display** rules (`Enabled if` / `Visible if`) that switch a value on or off depending on what else was submitted, and
- the **warning** rule that surfaces a non-blocking notice without rejecting the submission.

You write rules in a tiny expression language explained below. It looks like a calculator: numbers, comparisons, `and`/`or`, parentheses, a sprinkling of functions for dates and presence checks. No code. No deployment.

Long rules can be broken across multiple lines for readability — the system normalises whitespace before evaluation, so indent and line-break however helps you read them.

> [!TIP]
> You don't have to save and submit to try a rule out. The schema editor's **Preview** button (see [Previewing a schema](schemas.md#previewing-a-schema)) renders the live form from your unsaved schema and evaluates these rules in the browser as you type. It's a best-effort approximation — the server stays authoritative, and a few helpers (`sampleTimestamp()`, `serviceName()`) and the regex dialect differ client-side — so verify anything important with a real submission too.

> [!NOTE]
> **Rules don't run on drafts.** A submission [saved as a draft](submissions.md#saving-a-draft) is only checked for shape (type, min/max, length, regex) on the values that were filled in. The rules described here — value-level, schema-level, `Enabled if` / `Visible if`, and warnings — plus required-value and cadence checks are all skipped until the draft is published, at which point the full pipeline runs.

## Why bother

The built-in `min`, `max`, `regex` and friends are enough for the obvious shape checks. Validation rules buy you the rest:

- *"This value can only be reported on a weekday."*
- *"Expenses cannot exceed revenue."*
- *"Either both `start_date` and `end_date` are present, or neither is."*
- *"Tonnes collected must be at least 50% of last week's complaints count."* (well, almost — see the *what isn't possible* section.)
- *"Show a friendly error message instead of the generic 'invalid input'."*

Any rule that can be expressed as "given these numbers/dates/strings, is this OK?" fits.

## Two ways your rule can answer

Whatever you write, the system asks the same question: *"does this rule consider the input valid?"* You can answer in two styles. Both are valid; mix and match.

### Boolean style — terse

Return `true` for valid, `false` for invalid. Whoever submits the data gets a generic error like *"Value 'monthly / tonnes' value-validation failed: expression returned false"*.

```text
tonnes >= 0
```

If the rule returns `true`, all good. If it returns `false`, the submission is rejected.

### Error-message style — friendly

Return a **non-empty string** to reject and use that string as the error message verbatim. Return an empty string (`''`) or `null` to say "all good".

```text
if(tonnes < 0, 'tonnes cannot be negative', null)
```

When `tonnes` is `-3`, the submitter sees `Value 'monthly / tonnes' value-validation failed: tonnes cannot be negative`. When `tonnes` is `12`, the rule returns `null` and the sample is accepted.

The error-message style is almost always worth it for anything user-facing — a clear message saves a support round-trip.

## What rules can see

Every rule — value-level validation, schema-level validations, conditional display (`Enabled if` / `Visible if`), Warning — evaluates against the **same** unified context. Two kinds of names are available:

| Name shape                | What it is                                                                                          | Always there?                                                            |
|---------------------------|-----------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------|
| `<value_name>`            | The submitted sample value for the schema value with that name. `null` when not submitted.          | One key per declared value in the schema.                                |
| `[<value_name>.minimum]`  | The `Min` bound configured for that value.                                                          | Only for numeric (`Integer` / `Number`) values that have `Min` set.      |
| `[<value_name>.maximum]`  | The `Max` bound configured for that value.                                                          | Only for numeric (`Integer` / `Number`) values that have `Max` set.      |

Plus every [built-in function](#built-in-functions) below — including the [`latest()` / `previous()` history functions](#history-last-submitted-values), the one way a rule can look beyond the current submission.

A few things to notice:

- The bound keys use NCalc's bracketed-identifier form (`[...]`) because the `.` separator isn't allowed in plain identifiers. The square brackets are part of the syntax — you write `[tonnes.maximum]`, not `tonnes.maximum`.
- There's no `value` / `minimum` / `maximum` shorthand. A rule on the `tonnes` value refers to its own data as `tonnes` and to its configured cap as `[tonnes.maximum]`. This keeps every rule unambiguous about which value it's talking about and makes value-level rules look just like schema-level ones.
- Sibling values are reachable from every rule. The rule on `recycling_tonnes` can compare `recycling_tonnes` against `total_tonnes` directly.
- Missing values arrive as `null`, so writing `siblingThatMayBeMissing > 0` quietly evaluates to `false` instead of throwing. Use `isNull(...)` when you need to distinguish "not submitted" from "submitted as zero".

## Value-name format

Names must be valid C-style identifiers: start with a letter or underscore, contain only letters, digits, and underscores. That's it.

- `tonnes_collected`, `_internal`, `RouteCount`, `kpi_1` — all fine.
- `tonnes.collected`, `tonnes-collected`, `tonnes collected`, `2nd_check` — all rejected by the schema editor.

The reason is mechanical: rules reference values by name, and the bound namespace lives at `[name.minimum]` / `[name.maximum]`. Allowing `.` (or other non-identifier characters) in value names would either force NCalc's bracket form for ordinary references or, worse, make `[foo.bar.maximum]` ambiguous between "the maximum of `foo.bar`" and "a value literally named `foo.bar.maximum`". The C-identifier rule also keeps the same names usable verbatim in C#, JavaScript, and any other language that consumes the OData feed.

## Sample-level rules (one value at a time)

Edit a schema value and look for the **value validation** field. Whatever you put there runs **once for every submitted sample** of that value.

A few common patterns (assume each example lives on the value whose name you see in the rule):

**Reject negatives, friendly message.**

```text
if(tonnes < 0, 'tonnes cannot be negative', null)
```

**Reject anything outside the configured min, with a custom message.**

```text
if(tonnes < [tonnes.minimum], 'value below the configured minimum', null)
```

(You probably don't need this — the schema editor's `Min` field rejects it for free. Use it only when you want a *different* message.)

**Warn early when a value is creeping toward the cap.**

```text
if([tonnes.maximum] - tonnes < 5,
   'within 5 t of the cap — recheck the sample',
   null)
```

**Soft cap with a warning that explains where the limit comes from.**

```text
if(tonnes > 200, 'value exceeds the safe threshold (200 t per truck per day)', null)
```

**Weekday-only date.** Assume `report_date` is a `Date` and shouldn't fall on a weekend.

```text
if(dayOfWeek(report_date) == 0 or dayOfWeek(report_date) == 6,
   'date must be a weekday',
   null)
```

**Postcode-ish format.** Assume `postcode` is a `String`. Combined with a `regexPattern` this is usually overkill, but works:

```text
if(len(postcode) != 6, 'postcode must be exactly 6 characters', null)
```

**No future timestamps.** `submitted_at` is a `Date`.

```text
if(submitted_at > now(), 'date cannot be in the future', null)
```

**Sanity check on a percentage** — `contamination_pct` is a `Number` and should be 0..100.

```text
contamination_pct >= 0 and contamination_pct <= 100
```

**Combine two checks with friendly messages.**

```text
if(contamination_pct < 0,    'cannot be negative',
if(contamination_pct > 100,  'cannot exceed 100',
null))
```

(`if(...)` calls can be nested; format them however you like, whitespace and line breaks don't matter.)

**Reject empty strings even when not required.**

```text
if(len(reason) == 0, 'cannot be blank', null)
```

> `null` values reach the rule too — a `null` always means "the sample's value was left empty". If the value isn't required, your rule should accept `null` (most do, because comparisons with `null` are `false`). If you want to reject blanks explicitly, use `isNull(...)`:
>
> ```text
> if(isNull(reason), 'reason is required', null)
> ```

## Conditional display (`Enabled if` / `Visible if`)

Sometimes a value only makes sense when *another* value is present (or has a particular shape). The two **conditional display** fields on a value let you express that without splitting the schema in two:

| Field         | UI behaviour                                                                              | Submission behaviour                                                                                            |
|---------------|-------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------|
| `Visible if`  | When the rule is **false**, the input is hidden from the submission form entirely.        | When the rule is **false**, the sample is silently dropped and a warning is added to the response.              |
| `Enabled if`  | When the rule is **false**, the input is rendered read-only with a "disabled" badge.       | Same as `Visible if` — the sample is dropped with a warning.                                                    |

Server-side both fields behave identically: a false-y result discards the corresponding sample (it is **not** persisted) and emits a warning on the response. The difference is purely cosmetic — pick whichever feels right.

Inside the rule, every value declared by the parent schema is available by name (see [What rules can see](#what-rules-can-see)) — including the current value, referenced by its own name.

A few examples:

**Only collect notes when there was an incident.** Schema has `incident_count` (`Integer`) and `incident_notes` (`String`).

```text
incident_count > 0
```

Add that to the `Visible if` of `incident_notes`. When `incident_count` is 0 or empty, the notes input disappears; whoever submits via API gets a warning if they include notes anyway. *Required* values whose `Visible if` / `Enabled if` evaluates to false are exempt from the "required" check — they are conditionally optional.

**Suppress a deprecated value for a specific service.**

```text
serviceName() != 'pilot-roads'
```

Add that to `Enabled if`. The `pilot-roads` service sees the input greyed out; everyone else fills it in normally.

**Cross-value gate.** Schema has `transport_mode` (`String`) and `fuel_type` (`String`). `fuel_type` only makes sense when `transport_mode` is `'truck'`:

```text
transport_mode == 'truck'
```

> Conditional display rules run **before** any other validation. A sample whose `Enabled if` / `Visible if` is false is dropped immediately — its shape, range, regex and value-level rules are never evaluated. That keeps "irrelevant in this context" cleanly separate from "wrong shape".

## Warnings (non-blocking notices)

Validation rules reject; sometimes you only want to *flag* something. The **Warning** field on a value handles that:

- Return **`false`**, **`null`**, or an empty string for "no warning".
- Return **`true`** for "warning, with a default message".
- Return a **non-empty string** for "warning, with that text".

When a warning fires the submission is still accepted — the warning text simply appears alongside the success response (and in the admin UI as the user types).

Use this for soft thresholds and editorial nudges:

**Heads-up when a value is unusually large but still legal.** (Rule on the `tonnes` value.)

```text
if(tonnes > 200, 'value exceeds the typical range (above 200 t)', null)
```

**Suggest a follow-up.** (Rule on `incident_notes`, comparing against the sibling `incident_count`.)

```text
if(incident_count > 0 and isNull(incident_notes),
   'consider adding notes when incidents are reported',
   null)
```

**Cross-value sanity check.** (Rule on `sick_leave`, comparing against the sibling `employees_active`.)

```text
if(employees_active > 0 and sick_leave / employees_active > 0.2,
   'more than 20% of the workforce out sick — please check for an outbreak',
   null)
```

**Bare boolean.** Just fires the default warning text.

```text
tonnes > 200
```

The Warning rule sees the same context as every other rule (see [What rules can see](#what-rules-can-see)), plus all the [built-in functions](#built-in-functions). Multiple values can fire warnings on the same submission — they all show up on the response.

## Schema-level rules (cross-value)

Edit a schema and look for the **schema-level validations** field. You can add **many** rules here; each one runs **once per submission** that touches the schema. Inside the rule, every value defined by the schema is available as a variable named after its `name` (the machine-style identifier you gave it in the value editor, **not** the label), and numeric bounds are reachable as `[name.minimum]` / `[name.maximum]` — same unified context every other rule sees.

If a particular value wasn't included in the submission, its variable is `null`.

The system also gives you helpers for the submitted samples themselves (timestamps, notes) — see [Built-in functions](#built-in-functions) below.

Some common patterns:

**Mutually consistent totals.** A schema has `revenue` and `expenses` (both `Number`).

```text
if(expenses > revenue, 'expenses cannot exceed revenue', null)
```

**A simple sum check.** A schema has `households`, `businesses`, and `total_collected`. The total should match the sum.

```text
if(total_collected != households + businesses,
   'total_collected must equal households + businesses',
   null)
```

For floating-point tolerance, allow a small margin:

```text
if(total_collected - (households + businesses) > 0.5
   or total_collected - (households + businesses) < -0.5,
   'total_collected differs from the sum by more than 0.5',
   null)
```

**Mutual presence — both, or neither.** A schema has `start_date` and `end_date`.

```text
if(isNull(start_date) != isNull(end_date),
   'start_date and end_date must both be set or both be empty',
   null)
```

**Ordered dates.** Same schema.

```text
if(not isNull(start_date) and not isNull(end_date) and end_date < start_date,
   'end_date cannot be before start_date',
   null)
```

(The two `isNull` guards stop the comparison from misfiring when one of the values is empty.)

**Only one of several.** A schema has `cash`, `card`, `transfer` and exactly one should be filled in.

```text
if(
   (isNull(cash) and isNull(card) and isNull(transfer))
   or (not isNull(cash) and not isNull(card))
   or (not isNull(cash) and not isNull(transfer))
   or (not isNull(card) and not isNull(transfer)),
   'set exactly one of: cash, card, transfer',
   null)
```

**Day-of-week guard for the whole submission.** Reject submissions whose `metric` sample was reported on a weekend.

```text
if(not isNull(sampleTimestamp('metric'))
   and (dayOfWeek(sampleTimestamp('metric')) == 0
        or dayOfWeek(sampleTimestamp('metric')) == 6),
   'metric can only be reported on a weekday',
   null)
```

**Multiple rules on one schema.** You can attach as many as you like. Each is evaluated independently; the submission is accepted only if every rule says "valid". Errors from all failing rules are returned to the caller in one shot, so submitters can fix everything in one round-trip.

```
revenue >= 0
expenses >= 0
if(expenses > revenue, 'expenses cannot exceed revenue', null)
if(total_units != households + businesses, 'total_units must equal households + businesses', null)
```

## Expression syntax

Quick reference for what you can put in a rule.

### Literals

| Literal              | Example                       | Notes |
|----------------------|-------------------------------|-------|
| Integer              | `42`, `-1`                    | |
| Decimal              | `3.14`, `0.5`                 | Use a dot, never a comma. |
| String               | `'hello'`                     | Single quotes. Use `''` to embed a quote. |
| Boolean              | `true`, `false`               | |
| Nothing / empty      | `null`                        | What you get for unset optional values. |

### Arithmetic operators

| Operator | Means                  |
|----------|------------------------|
| `+`      | Addition (or string concatenation when both sides are strings). |
| `-`      | Subtraction or negation. |
| `*`      | Multiplication.        |
| `/`      | Division.              |
| `%`      | Remainder (modulo).    |

### Comparison operators

| Operator       | Means                |
|----------------|----------------------|
| `==`, `=`      | Equal to             |
| `!=`, `<>`     | Not equal to         |
| `<`, `<=`      | Less than (or equal) |
| `>`, `>=`      | Greater (or equal)   |

> Both equality spellings work. `==` is the recommended one because `=` looks like assignment in many other languages and that can be confusing.

### Logical operators

| Operator   | Means    |
|------------|----------|
| `and`, `&&`| Both true |
| `or`, `\|\|`| Either true |
| `not`, `!` | Negation |

Use parentheses freely to make precedence obvious — `not a or b` and `not (a or b)` are very different things.

### Conditional

```text
if(condition, then_value, else_value)
```

Standard ternary. Returns `then_value` when the condition is true, otherwise `else_value`. Nest them for chains (rule on the `score` value):

```text
if(score < 0,   'negative',
if(score == 0,  'zero',
if(score > 100, 'too big',
                null)))
```

### Membership

```text
in(colour, 'red', 'green', 'blue')
```

True when `value` equals any of the listed options. Useful for whitelists:

```text
if(not in(status, 'open', 'in_progress', 'closed'),
   'status must be one of: open, in_progress, closed',
   null)
```

### Names are case-insensitive

`Tonnes`, `tonnes`, and `TONNES` all refer to the same thing. Same for function names and bound keys: `dayOfWeek(report_date)`, `dayofweek(report_date)`, `DAYOFWEEK(report_date)` all work, and `[tonnes.maximum]` matches `[Tonnes.MAXIMUM]`.

It's still a good idea to pick one style per schema and stick to it.

### Whitespace and line breaks

The system doesn't care. Indent and break long expressions however you like — the rule editor stores your formatting verbatim, and newlines are collapsed to spaces before evaluation. As you type, the schema editor sends each rule through a server-side **syntax check** and shows a small green confirmation under the field (or a red explanation when something doesn't parse). Unknown identifiers and function names are not flagged here — they're caught when you save the schema. Use that to keep nested `if(...)` chains readable:

```text
if(
   expenses > revenue,
   'expenses cannot exceed revenue (' + expenses + ' > ' + revenue + ')',
   null
)
```

(That example also shows how to use `+` to splice numbers into a message — they're coerced to strings when concatenated with a string.)

## Built-in functions

Available in **both** sample-level and schema-level rules unless noted otherwise.

### Dates and times

| Function              | Returns | Notes |
|-----------------------|---------|-------|
| `now()`               | current UTC date+time | |
| `today()`             | current UTC date at 00:00 | |
| `dayOfWeek(d)`        | 0..6 — **Sunday = 0**, Monday = 1, …, Saturday = 6 | |
| `dayOfMonth(d)`       | 1..31  | |
| `dayOfYear(d)`        | 1..366 | |
| `weekOfYear(d)`       | 1..53 — ISO 8601 week number | |
| `month(d)`            | 1..12  | |
| `year(d)`             | full year (e.g. 2026) | |
| `hour(d)`             | 0..23  | |
| `minute(d)`           | 0..59  | |
| `second(d)`           | 0..59  | |

All of these accept a value of `Date` type, or a string that looks like a date.

### Presence and length

| Function                   | Returns | Notes |
|----------------------------|---------|-------|
| `isNull(x)`                | `true` if `x` is missing/empty | Use this before comparing values that may be absent. |
| `coalesce(a, b, …)`        | the first non-empty argument | Returns `null` if all are empty. |
| `len(x)`                   | string length (or collection size) | Errors out for non-string non-collection inputs. |

### Context (who/what is being validated)

| Function                | Returns | Available |
|-------------------------|---------|-----------|
| `serviceName()`         | machine-style name of the submitting service | both levels |
| `schemaName()`          | machine-style schema name | both levels |
| `valueName()`           | the value being validated | **sample level only** |
| `sampleTimestamp()`     | timestamp of the sample being validated | **sample level** |
| `sampleNote()`          | note attached to the sample being validated | **sample level** |
| `sampleTimestamp('x')`  | timestamp of the schema's value named `'x'` in this submission (or `null` if not present) | **schema level** |
| `sampleNote('x')`       | same, for the note | **schema level** |

> The `sampleTimestamp('x')` / `sampleNote('x')` forms are particularly useful in schema-level rules where you want to reason about *when* a particular value was reported, not just what it was. Pair them with `isNull(...)` to guard against the value being absent.

### History (last submitted values)

These two functions let a rule compare the value being submitted now against what the **same service** reported before. They only ever see *live* data — submissions that are approved, or that never needed approval. Anything still awaiting approval, or rejected, is invisible to them.

| Function                  | Returns | Available |
|---------------------------|---------|-----------|
| `latest('x')`             | the most recent live value for `'x'`, regardless of how long ago | both levels |
| `latest('x', fallback)`   | same, but returns `fallback` instead of `null` when there's no history | both levels |
| `previous('x')`           | the live value for `'x'` in the cadence period **immediately before** the one this submission targets (`null` if that period had no submission) | both levels |
| `previous('x', fallback)` | same, but returns `fallback` instead of `null` | both levels |

A few details worth knowing:

- **Value level shorthand.** In a value-level rule you can drop the name: `latest()` / `previous()` mean "this value". To supply a fallback for the current value, pass it as the only argument: `latest(0)`.
- **Which period `previous()` looks at** is driven by the named value's own cadence (daily, weekly, monthly, …) and the timestamp of this submission. For a weekly value submitted this week, `previous('x')` is last week's value.
- **Missing history is `null`.** A brand-new service, or a gap in reporting, makes both functions return `null` (or your fallback). As everywhere else, comparisons against `null` are neither true nor false — guard with `isNull(...)` or supply a fallback.
- **Editing an existing submission** doesn't count as its own history: when you replace a submission, `latest()` / `previous()` look past it to the genuinely prior value.
- **Preview is approximate.** The in-browser preview can't reach the database, so `latest()` / `previous()` there return your fallback (or `null`). Verify history-based rules with a real submission.

## Common recipes

### Range with a friendly message that includes the offending number

(Rule on the `score` value.)

```text
if(score < 0 or score > 100,
   'score must be between 0 and 100 (got ' + score + ')',
   null)
```

### "Required when X is set"

A schema has `incident_count` and `incident_notes`; notes are only required when the count is greater than zero.

```text
if(incident_count > 0 and (isNull(incident_notes) or len(incident_notes) == 0),
   'incident_notes is required when incident_count > 0',
   null)
```

### Weekend-only / weekday-only

(Rule on the `report_date` value.)

```text
// weekdays only
dayOfWeek(report_date) != 0 and dayOfWeek(report_date) != 6

// weekends only
dayOfWeek(report_date) == 0 or dayOfWeek(report_date) == 6

// first week of the month only (whatever timezone you happen to be using server-side)
dayOfMonth(report_date) <= 7
```

### "Use the right schema for the right week"

Reject submissions that arrive in the wrong ISO week of the year. Useful for catching pipelines that resubmit stale data.

```text
if(weekOfYear(sampleTimestamp()) != weekOfYear(now()),
   'sample timestamp is not in the current ISO week',
   null)
```

### One-of-N

```text
if(not in(category, 'roads', 'waste', 'parks', 'water'),
   'category must be one of: roads, waste, parks, water',
   null)
```

### Number-precision tolerance

When comparing numbers that come from accumulated floats, allow a tiny margin. Express it manually with two checks (one for either direction):

```text
if(declared_total - (a + b + c) > 0.5 or declared_total - (a + b + c) < -0.5,
   'declared_total differs from the sum of components by more than 0.5',
   null)
```

### Service-specific tweaks

Validation rules see the submitting service's name via `serviceName()`. Use sparingly — it's a sign your schema is doing too much — but sometimes you really do need it:

(Rule on the `tonnes` value.)

```text
if(serviceName() == 'pilot-roads' and tonnes > 50,
   'pilot service is capped at 50 t per submission',
   null)
```

### Compare against the last reported value

Flag a suspicious jump from the previous live value (rule on the `tonnes` value):

```text
if(not isNull(latest()) and tonnes > latest() * 1.1,
   'more than 10% above the last reported figure — please double-check',
   null)
```

### Enforce a non-decreasing counter

For a meter reading that should never go backwards, treat a missing history as zero:

```text
if(reading < previous('reading', 0),
   'reading is lower than last period — meters do not run backwards',
   null)
```

## What isn't (yet) possible

To keep the editor self-contained and side-effect-free, validation rules **don't** have access to:

- Other services' data. Rules only ever see the current submitter's own data.
- The database directly. There's no `query(...)` function — by design.
- HTTP or anything external.

There is **one** controlled exception to "this submission only": the [`latest()` / `previous()` history functions](#history-last-submitted-values) can read the submitting service's own last *live* (approved / not-required) values. Beyond that, validation rules are **pure** functions of the submission, its own history, and the current time — which keeps them cheap (a few milliseconds) and predictable.

## Troubleshooting

**"Value 'monthly / tonnes' value-validation failed: expression returned false".**
Your rule returned `false`. Either flip it to the error-message style and ship a friendlier message, or check whether the boolean logic is what you think it is. If you mean *"reject when condition holds"*, write `if(condition, 'message', null)`, not bare `condition`.

**"Value 'monthly / tonnes' value-validation error: …" (with an actual technical error).**
The expression couldn't be evaluated at all. Most common causes:

- Misspelled variable. Remember names are case-insensitive but they must exist — `revunue` won't be silently treated as `revenue`; it'll be `null`. The error normally says *"Unknown function or variable"*.
- Wrong argument type to a function. `dayOfWeek('not-a-date')` blows up.
- Mismatched parentheses.

**Comparisons against `null` always look false.**
That's how the language works: `null > 0` is not `true`, it's "neither true nor false", and the rule ends up taking the "false" branch in `if(...)`. Always guard with `isNull(...)` before comparing a value that might be missing.

**Two rules that contradict each other.**
The submission is rejected and **both** errors are returned to the submitter. Fix whichever is wrong, or remove one of them.

**A rule worked last week and now it doesn't.**
The only thing that could have changed under your feet is the schema itself — you (or another admin) probably renamed a value. Schema-level rules reference values by **name** (the machine-style identifier), so renaming a value silently turns its reference inside every rule into `null`. Search the rules in this schema for the old name and update them.

**Submission says "Sample 'monthly / tonnes' discarded: …" but I posted a value for it.**
The value's `Enabled if` / `Visible if` rule evaluated to false in the context of the rest of the submission, so the system dropped the sample on purpose. Check the rule with the other values you sent in mind. Conditional-display rules are evaluated before anything else, including required-value checks.

**My Warning rule never fires.**
A bare condition like `tonnes > 200` fires when the result is `true`. A `if(condition, 'message', null)` rule fires only when the `then` branch returns a non-empty string. If you wanted a default message and got nothing, return `true` directly.

**"Parameter X not defined".**
The rule references a value that isn't declared on the schema. The unified context exposes every declared value by its `name` (`null` when not submitted) — typos return `null`, but referring to something that was never declared raises this error. Common causes: a sibling value was renamed, or you wrote `[tonnes_collected.max]` instead of `[tonnes_collected.maximum]`.

**Submission says "Value name '…' is not a valid identifier".**
The schema editor rejects value names that don't follow the C-identifier rule (letters/digits/underscores, no leading digit). See [Value-name format](#value-name-format).

## Where to next

- [schemas.md](schemas.md) — where the rule fields live in the UI, and the broader schema-design picture.
- [submissions.md](submissions.md) — exercise rules through the on-behalf-of submission form to confirm they behave as you expect.
- [../client/api.md § Validation expressions](../client/api.md#validation-expressions) — what the API caller sees when a rule rejects them.
- [../architecture/architecture.md § Validation](../architecture/architecture.md#validation) — where rules fit in the broader validation pipeline (developers only).

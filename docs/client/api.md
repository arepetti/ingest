# API reference for service clients

This is the reference for the endpoints a **service** account is expected to call: discover the schemas you're allowed to submit against, post or replace submissions, look up your own data, and check your submission status against the configured cadences. Admin/operator endpoints are documented at a higher level in [../admin-user-guide/README.md](../admin-user-guide/README.md) — the live Swagger UI (`/swagger`) is the source of truth for those.

> **Tip.** Every endpoint described here is mirrored in the OpenAPI document at `GET /swagger/v1/swagger.json`. Copy it into your client generator of choice if you want strongly-typed bindings.

If you're just getting started — including how to obtain a key in the first place — read [README.md](README.md) first.

## Conventions

- **Base URL** depends on your deployment. The examples use `https://ingest.example.org`.
- **Authentication** is via API key in the `X-Api-Key` header. See [../architecture/authentication.md](../architecture/authentication.md). Every service-facing endpoint requires it; the only anonymous endpoint today is `POST /api/expressions/translate`, used internally by the admin SPA.
- **Time** is always UTC ISO-8601 strings on the wire (e.g. `2026-05-12T08:00:00Z`).
- **Enums** are serialised as their string names. `"Service"`, not `0`.
- **Errors** use [RFC 7807 problem-details JSON](https://www.rfc-editor.org/rfc/rfc7807):
  ```json
  {
    "title": "Validation failed",
    "status": 400,
    "detail": "One or more values failed validation.",
    "errors": [
      "Value 'monthly_kpis.tonnes' below min (0)."
    ]
  }
  ```
  The `errors` extension is only present on 400 validation responses.

## Common status codes

| Code | Meaning |
|------|---------|
| **200 OK** | Request succeeded, body carries the response. |
| **201 Created** | Resource created; `Location` header points to it. |
| **204 No Content** | Mutation succeeded, no body. |
| **400 Bad Request** | Validation failed. Look at `errors[]` for the list. |
| **401 Unauthorized** | Missing/invalid `X-Api-Key`. The `WWW-Authenticate` header tells you which header to send. |
| **403 Forbidden** | The credential is valid but lacks permission (missing capability, foreign resource, or cadence window closed). |
| **404 Not Found** | The referenced resource doesn't exist, or isn't visible to you. |
| **409 Conflict** | Uniqueness collision (mostly relevant to admins). |
| **500 Internal Server Error** | Unhandled exception. Check the server logs. |

## Paging

Listing endpoints accept the following query parameters:

| Param          | Default | Notes |
|----------------|---------|-------|
| `page`         | `1`     | 1-based page number. |
| `pageSize`     | `50`    | Max page size depends on the endpoint; defaults are sane. |
| `sort`         | varies  | Endpoint-specific. `createdAt` returns newest-first on most listings. |
| `includeDeleted` | `false` | Soft-deleted entries are excluded unless this is `true`. Service-facing endpoints usually don't expose this. |

The response shape is always:

```json
{
  "items": [ ... ],
  "total": 1234,
  "page": 1,
  "pageSize": 50
}
```

---

## `GET /api/me`

Identifies the account behind the supplied API key. Useful for verifying credentials before doing real work and for adapting client behaviour to the role/kind/capabilities.

**Request**

```http
GET /api/me HTTP/1.1
X-Api-Key: abc12345.7N3pK0M9C0LSx0OqGZpY3vW0eFkdsbVz
```

**200 OK**

```json
{
  "id":    "1d4a3b5a-…",
  "name":  "roads-team",
  "label": "Roads & Highways team",
  "role":  "Service",
  "kind":  "Application",
  "capabilities": [],
  "assignedServiceIds": []
}
```

`capabilities` is the account's **effective** capability set — the fine-grained permissions that actually govern what it may do (see [architecture/authentication.md § Authorisation: capabilities](../architecture/authentication.md#authorisation-capabilities)). A pure `Service` account has none (it uses the self-service `/api/me*`, `/api/schemas*` and `/api/submissions*` endpoints, which don't require a capability); back-office accounts list strings such as `"submissions:read"` or `"schemas:manage"`. The SPA uses this array to decide which navigation and actions to render. The payload also carries feature flags (e.g. `approvalEnabled`, `emailEnabled`) used by the admin UI.

`assignedServiceIds` is the account's **service scope** (see [architecture/authentication.md § Authorisation: service scope](../architecture/authentication.md#authorisation-service-scope)). An **empty** array means *unrestricted* — the account sees every service. A **non-empty** array confines every cross-service read (the submissions list, status/missing reports, Explore, the OData feed and the ad-hoc query) to those service ids; `Admin` accounts are always unrestricted and never carry a scope here.

**Status codes**

| Code | When |
|------|------|
| 200  | Always when the key is valid. |
| 401  | Missing or invalid `X-Api-Key`. |

---

## `GET /api/me/status`

Returns the submission-freshness snapshot for the calling account: for every value of every visible schema, the current cadence bucket, the most recent submitted sample in that bucket (if any), and a `satisfied` flag.

**Query parameters**

| Param    | Values | Default |
|----------|--------|---------|
| `period` | `day`, `week`, `fortnight`, `month`, `quarter`, `halfyear` (alias: `semiannual`), `year` | configured `Ingest:DefaultStatusPeriod` (typically `week`). |

The `period` hint is only used to render summary headers — per-value satisfaction is always computed against the value's own cadence bucket regardless.

**Request**

```http
GET /api/me/status?period=month HTTP/1.1
X-Api-Key: ...
```

**200 OK**

```json
{
  "serviceId":   "1d4a3b5a-…",
  "serviceName": "roads-team",
  "period":      "month",
  "schemas": [
    {
      "schemaName": "monthly_kpis",
      "label":      "Monthly KPIs",
      "enabled":    true,
      "values": [
        {
          "valueName":     "tonnes",
          "label":         "Tonnes collected",
          "cadence":       "Monthly",
          "required":      true,
          "enabled":       true,
          "periodStart":   "2026-05-01T00:00:00Z",
          "periodEnd":     "2026-06-01T00:00:00Z",
          "lastSubmissionId": "f0e9d8c7-…",
          "lastTimestamp":    "2026-05-12T08:00:00Z",
          "satisfied":     true
        },
        {
          "valueName":  "downtime_hours",
          "label":      "Equipment downtime (hours)",
          "cadence":    "Weekly",
          "required":   false,
          "enabled":    true,
          "periodStart":"2026-05-25T00:00:00Z",
          "periodEnd":  "2026-06-01T00:00:00Z",
          "lastSubmissionId": null,
          "lastTimestamp":    null,
          "satisfied":  false
        }
      ]
    }
  ]
}
```

A value with `enabled: false` is still returned so clients can render a complete UI — filter them out in the client when displaying.

**Status codes** — 200, 401.

---

## `GET /api/schemas`

Lists every schema visible to the calling account: global ones and any whose `ServiceIds` list explicitly names the caller.

**Request**

```http
GET /api/schemas HTTP/1.1
X-Api-Key: ...
```

**200 OK** — array of schemas, each in the same shape returned by `/api/schemas/{name}` (see below).

**Status codes** — 200, 401.

---

## `GET /api/schemas/{name}`

Fetch a single schema by name.

**Request**

```http
GET /api/schemas/monthly_kpis HTTP/1.1
X-Api-Key: ...
```

**200 OK**

```json
{
  "id":           "9a8b7c6d-…",
  "name":         "monthly_kpis",
  "label":        "Monthly KPIs",
  "description":  "Standard monthly KPI package.",
  "notes":        null,
  "modifiable":   true,
  "enabled":      true,
  "submissionValidations": [
    "if(tonnes < 0, 'tonnes cannot be negative', null)",
    "downtime_hours <= 168 or 'downtime cannot exceed one week'"
  ],
  "isGlobal":     true,
  "serviceIds":   [],
  "values": [
    {
      "name":     "tonnes",
      "label":    "Tonnes collected",
      "description": null,
      "notes":    null,
      "caption":  "Collection metrics",
      "type":     "Number",
      "unit":     "t",
      "cadence":  "Monthly",
      "required": true,
      "modifiable": true,
      "enabled":  true,
      "min":      0,
      "max":      null,
      "minDate":  null,
      "maxDate":  null,
      "minLength":null,
      "maxLength":null,
      "regexPattern": null,
      "valueValidation": null,
      "warning": null,
      "enabledIf": null,
      "visibleIf": null,
      "sinceVersion": null
    }
  ],
  "layout": [
    { "kind": "section", "caption": "Collection metrics", "items": [
      { "kind": "value", "valueName": "tonnes" }
    ]}
  ],
  "version":           1,
  "versionModifiedAt": "2026-01-15T10:00:00Z",
  "createdAt":  "2026-01-15T10:00:00Z",
  "createdBy":  "admin",
  "modifiedAt": "2026-04-20T14:30:00Z",
  "modifiedBy": "admin"
}
```

> A few fields are **presentational only** — they are part of the schema definition but the server never inspects them when accepting a submission. Programmatic clients can safely ignore them:
>
> - On each value: `label`, `description`, `notes`, and `caption` (the optional heading the admin SPA renders above a value in submission forms).
> - At the schema level: `layout` (the UI grouping tree the admin SPA renders as sections in submission forms) and `versionModifiedAt` (server-managed timestamp anchoring the admin SPA's time-limited "New" badge).
>
> `version` and per-value `sinceVersion` are informational for clients but server-validated: `version` is monotonic and `sinceVersion` must satisfy `0 ≤ sinceVersion ≤ schema.version`.

**Status codes**

| Code | When |
|------|------|
| 200  | Schema returned. |
| 404  | No schema with that name, or not visible to the caller. |

---

## `GET /api/schemas/{name}/example`

Build an example submission body for a schema. One sample per declared value, with a sensible default per type — empty string for `String`, `0` (or the value's `min`) for numerics, today (or `minDate`) for `Date`, `false` for `Boolean`. Validation rules are **intentionally ignored** — the goal is to give callers a starting template, not a guaranteed-valid submission.

The schema's visibility rule applies (audience must include the caller).

**Request**

```http
GET /api/schemas/monthly_kpis/example HTTP/1.1
X-Api-Key: ...
```

**200 OK**

```json
{
  "samples": [
    { "schemaName": "monthly_kpis", "valueName": "tonnes",         "value": 0,      "timestamp": "2026-05-26T00:00:00Z", "note": null },
    { "schemaName": "monthly_kpis", "valueName": "downtime_hours", "value": 0,      "timestamp": "2026-05-26T00:00:00Z", "note": null }
  ]
}
```

**Status codes**

| Code | When |
|------|------|
| 200  | Example body returned. |
| 404  | No schema with that name, or not visible to the caller. |

---

## `POST /api/expressions/validate`

Anonymous syntax check for a validation expression. Returns `{ "ok": true }` when the parser accepts the input, or `{ "ok": false, "error": "…", "position": <0-based char offset?> }` when it doesn't. **Unknown identifiers and function names are not flagged here** — full validation runs when the schema is saved.

A failed syntax check is a normal outcome (not an HTTP error), so the endpoint always returns `200 OK` with a JSON body. Protocol errors (empty body, over-length input) still surface as `400`.

**Request**

```http
POST /api/expressions/validate HTTP/1.1
Content-Type: application/json
Accept: application/json

{ "expression": "value > 0 and value < 100" }
```

**200 OK (passing)**

```json
{ "ok": true }
```

**200 OK (failing)**

```json
{ "ok": false, "error": "Unexpected character at position 6.", "position": 6 }
```

**Status codes**

| Code | When |
|------|------|
| 200  | Syntax check ran (see `ok`). |
| 400  | Expression was empty or exceeded the length cap. |

---

## `POST /api/submissions`

Submit one or more samples for a schema. Every sample in a single payload must reference the same `schemaName`.

**Request**

```http
POST /api/submissions HTTP/1.1
X-Api-Key: ...
Content-Type: application/json

{
  "samples": [
    {
      "schemaName": "monthly_kpis",
      "valueName":  "tonnes",
      "value":      127.5,
      "timestamp":  "2026-05-12T08:00:00Z",
      "note":       "Includes overflow from neighbouring district."
    },
    {
      "schemaName": "monthly_kpis",
      "valueName":  "downtime_hours",
      "value":      4,
      "timestamp":  "2026-05-12T08:00:00Z",
      "note":       null
    }
  ]
}
```

`value` is typed according to the schema's value definition:

| Schema value type | JSON type expected           |
|-------------------|------------------------------|
| `String`          | string                       |
| `Integer`         | integer                      |
| `Number`          | number (float allowed)       |
| `Date`            | ISO-8601 string              |
| `Boolean`         | `true` / `false`             |

Send `null` to skip an optional value entirely (or just omit the sample).

**Query parameters**

| Param   | Default | Notes |
|---------|---------|-------|
| `draft` | `false` | When `true`, save as a work-in-progress **draft** (see [Drafts](#drafts-optional)). Validation is relaxed and the submission stays out of every live stream and the approval workflow until it's published. |

**201 Created**

```http
HTTP/1.1 201 Created
Location: /api/submissions/3e8a1f56-…

{
  "id": "3e8a1f56-…",
  "warnings": [
    "Sample 'monthly_kpis.notes' discarded: incident_count > 0",
    "Sample 'monthly_kpis.tonnes': value exceeds the typical range"
  ]
}
```

`warnings` is always present and is an empty array when nothing of note happened. See [Warnings](#warnings) below.

**Status codes**

| Code | When |
|------|------|
| 201  | Accepted; response carries the new submission id and any warnings. |
| 400  | Validation failed. Look at `errors[]`. |
| 401  | Missing/invalid key. |
| 404  | The schema you referenced is not visible to you. |

**Validation rules that produce 400**

The complete list, in the order they're applied (see [../architecture/architecture.md § Validation](../architecture/architecture.md#validation)):

- Schema must exist and be visible.
- Schema must be enabled; each referenced value must be enabled.
- Conditional display rules (`Enabled if` / `Visible if`): values whose rule evaluates to false are **silently dropped** with a warning (see [Warnings](#warnings)), not rejected.
- Value type must match (`Integer`, `Number`, `Date`, `String`, `Boolean`).
- `min`/`max`/`minLength`/`maxLength`/`regexPattern` constraints must hold.
- Per-value validation rule must pass (see [../admin-user-guide/validation.md](../admin-user-guide/validation.md)).
- No other live sample for the same `(schema, value)` must exist inside the same cadence bucket.
- Schema-level validation rules must pass.
- All `required` values of every schema the submission carries samples for must be present (on `POST` only; `PUT` does not enforce this). Schemas the service is assigned to but didn't include in this submission are not checked. Values whose `Enabled if` / `Visible if` rule is false are exempt.

---

## `PUT /api/submissions/{id}`

Replace an existing submission. Service callers can only replace a submission while its **cadence window is still open** — once the next bucket has started, the submission is effectively immutable for the service. (Admins use the parallel admin endpoint to bypass this restriction.)

**Request**

```http
PUT /api/submissions/3e8a1f56-… HTTP/1.1
X-Api-Key: ...
Content-Type: application/json

{
  "samples": [
    {
      "schemaName": "monthly_kpis",
      "valueName":  "tonnes",
      "value":      129.0,
      "timestamp":  "2026-05-12T08:00:00Z",
      "note":       "Re-weighed after recalibration."
    },
    {
      "schemaName": "monthly_kpis",
      "valueName":  "downtime_hours",
      "value":      4,
      "timestamp":  "2026-05-12T08:00:00Z",
      "note":       null
    }
  ]
}
```

**Query parameters**

| Param   | Default | Notes |
|---------|---------|-------|
| `draft` | `false` | Controls the [draft](#drafts-optional) state of the result. On a submission that is currently a draft, `draft=true` re-saves it as a draft and `draft=false` (the default) **publishes** it (running the full validation pipeline). A submission that has already been published **cannot** be returned to draft — `draft=true` against it is rejected with a 400. |

**200 OK**

```http
HTTP/1.1 200 OK

{
  "id": "3e8a1f56-…",
  "warnings": []
}
```

Same shape as the `POST` response — the submission id plus any non-blocking warnings. To fetch the post-replace state of the submission, follow up with `GET /api/submissions/{id}`.

**Status codes**

| Code | When |
|------|------|
| 200  | Replacement saved. |
| 400  | Validation failed, the cadence window has closed, **or** an attempt was made to return an already-published submission to draft. Each shows up as a validation error message. |
| 403  | The submission belongs to a different account. |
| 404  | No such submission, or no matching schema. |

> **Modifiability.** A schema value can be marked `modifiable: false`. Replacing such a value with a **different** sample is rejected; sending the unchanged sample (same value, timestamp, note) is accepted so retries don't fail.

---

## `POST /api/submissions/validate`

Run a **dry run**: validate a payload through the *exact* same pipeline as `POST /api/submissions` — schema visibility, type/range/regex shape, conditional display, per-value and schema-level rules, the cadence one-per-window duplicate check, required values, and the would-be approval decision — **without saving anything**. No submission, projection, audit entry, webhook, or email is produced.

This is the endpoint to call from your **integration tests / CI**: post a fixture and assert on `valid`. It accepts the same body and the same `draft` flag as `POST /api/submissions`.

**Request**

```http
POST /api/submissions/validate HTTP/1.1
X-Api-Key: ...
Content-Type: application/json

{
  "samples": [
    { "schemaName": "monthly_kpis", "valueName": "tonnes", "value": 127.5, "timestamp": "2026-05-12T08:00:00Z", "note": null }
  ]
}
```

**Query parameters**

| Param   | Default | Notes |
|---------|---------|-------|
| `draft` | `false` | When `true`, validate under the relaxed [draft](#drafts-optional) rules instead of a full publish. |
| `omit`  | *(none)* | Comma-separated list of checks to skip. Currently only `cadence` is recognised — `?omit=cadence` skips the context-dependent one-per-window duplicate check, so you can validate a fixture's **shape** in CI without it tripping on a period that's already been filled. Any other value is a 400. The parameter is designed to grow. |

**200 OK** — always 200 when the request was processed, *even when the payload is invalid*. Inspect `valid`:

```http
HTTP/1.1 200 OK

{
  "valid": false,
  "errors": [
    "Value 'monthly_kpis / tonnes' above max (100)."
  ],
  "warnings": [],
  "discardedSamples": [],
  "approvalStatus": "NotRequired",
  "requiredApprovers": []
}
```

| Field | Meaning |
|-------|---------|
| `valid` | `true` when a real submission of this payload would be accepted. |
| `errors` | Blocking validation errors (the same messages a real submit would return as a 400). Empty when `valid` is `true`. |
| `warnings` | Non-blocking diagnostics (fired `Warning` rules, `Enabled if` / `Visible if` discard notices). |
| `discardedSamples` | The `(schemaName, valueName)` pairs that would be dropped before persistence because their conditional-display rule is false. |
| `approvalStatus` | The approval state the submission would land in: `NotRequired` (live immediately) or `Pending` (held for approval). |
| `requiredApprovers` | The approvers that would govern the submission when it would be held for approval; empty otherwise. |

**Status codes**

| Code | When |
|------|------|
| 200  | Validation ran; read `valid` / `errors` for the verdict. |
| 400  | The request itself was malformed (e.g. an unrecognised `omit` value). Validation *failures* are **not** 400 here — they come back as `200` with `valid: false`. |
| 401  | Missing/invalid key. |

> A separate `POST /api/submissions/{id}/validate` validates a would-be **replacement** of an existing submission, mirroring `PUT /api/submissions/{id}` (including the cadence-window restriction). It returns `403`/`404` for the same reasons the real `PUT` does, and otherwise the same `200` verdict body.

---

## `GET /api/submissions/{id}`

Fetch one of your own submissions.

**Request**

```http
GET /api/submissions/3e8a1f56-… HTTP/1.1
X-Api-Key: ...
```

**200 OK**

```json
{
  "id":               "3e8a1f56-…",
  "serviceAccountId": "1d4a3b5a-…",
  "serviceName":      "roads-team",
  "samples": [
    { "schemaName": "monthly_kpis", "valueName": "tonnes",         "value": 127.5, "timestamp": "2026-05-12T08:00:00Z", "note": "…" },
    { "schemaName": "monthly_kpis", "valueName": "downtime_hours", "value": 4,     "timestamp": "2026-05-12T08:00:00Z", "note": null }
  ],
  "warnings": [],
  "submittedAt": "2026-05-12T09:01:33Z",
  "replacedAt":  null,
  "isDraft":     false,
  "createdAt":   "2026-05-12T09:01:33Z",
  "createdBy":   "roads-team",
  "modifiedAt":  "2026-05-12T09:01:33Z",
  "modifiedBy":  "roads-team",
  "isDeleted":   false
}
```

`isDraft` is `true` while the submission is a work-in-progress [draft](#drafts-optional) and `false` once it's a normal (published) submission. Legacy submissions that predate the field report `false`.

The `warnings` array is **persisted with the submission** and reflects whatever the validator produced at the last write (create or replace). It is always present; an empty array means "no warnings" — including for legacy submissions created before warnings were stored. This is the same content the write endpoints return (see [Warnings](#warnings)), kept on the record so it can be reviewed later.

**Status codes**

| Code | When |
|------|------|
| 200  | Found. |
| 404  | Doesn't exist or belongs to a different account. |

---

## `GET /api/submissions`

Page through your own submissions, optionally filtered by date.

**Query parameters**

| Param      | Notes |
|------------|-------|
| `page`     | 1-based, default 1. |
| `pageSize` | Default 50. |
| `sort`     | `createdAt` for newest-first; default is the repository's own ordering. |
| `from`     | Inclusive lower bound on `SubmittedAt`. |
| `to`       | Exclusive upper bound on `SubmittedAt`. |
| `draft`    | Restrict to [drafts](#drafts-optional) (`true`) or exclude them (`false`); omit to return both. |

**Request**

```http
GET /api/submissions?from=2026-05-01T00:00:00Z&to=2026-06-01T00:00:00Z&sort=createdAt HTTP/1.1
X-Api-Key: ...
```

**200 OK** — paged response (see [Paging](#paging)) where each item has the same shape as `GET /api/submissions/{id}`.

---

## Validation expressions

Schemas can attach custom validation rules at two levels — once per value, once per schema. From the **client** side all you really need to know is what the error response looks like:

- A rule that's satisfied is silent.
- A rule that fails contributes one entry to the 400 response's `errors[]` extension.
- Rule authors can choose between a terse boolean form (`value >= 0`) and a friendly error-message form (`if(value < 0, 'tonnes cannot be negative', null)`). When the friendly form is used, the string lands in `errors[]` verbatim.

If you author or maintain the schemas yourself, the full **rule-authoring guide** (operators, conditional `if(...)`, date helpers, null-safety, recipes) lives at [../admin-user-guide/validation.md](../admin-user-guide/validation.md). It explains how to write the rules from scratch with plenty of examples — designed for admins, not for the people calling the API.

---

## Warnings

Both `POST /api/submissions` and `PUT /api/submissions/{id}` return a `warnings: string[]` array in addition to the submission `id`. Warnings are **non-blocking** — the submission has already been accepted and persisted — but a well-behaved client should surface them to the operator, log them, or both. The array is always present and is empty when nothing of note happened.

The same warnings are **stored on the submission** and returned by `GET /api/submissions/{id}` (and the admin equivalents), so operators and admins can review them when inspecting a submission later — they are not just a one-off on the write response. Submissions created before this field existed report an empty array.

Warnings come from two places:

- **Conditional display.** A value's `Enabled if` or `Visible if` rule evaluated to false in the context of the rest of the submission. The corresponding sample was silently dropped (not persisted) and a warning explains why.
- **Per-value `Warning` rule.** A non-blocking sanity check the schema author attached to a value. Fires when the rule returns `true` or a non-empty string; in the latter case the string is used verbatim.

Example response with both kinds of warning:

```json
{
  "id": "3e8a1f56-…",
  "warnings": [
    "Sample 'monthly_kpis.incident_notes' discarded: incident_count > 0",
    "Sample 'monthly_kpis.tonnes': value exceeds the typical range (above 200 t)"
  ]
}
```

If you only care about errors, you can ignore the field. If you care about data quality, fail loudly on it. See the rule-authoring guide ([../admin-user-guide/validation.md § Conditional display](../admin-user-guide/validation.md#conditional-display-enabled-if--visible-if) and [§ Warnings](../admin-user-guide/validation.md#warnings-non-blocking-notices)) for the full semantics.

## Approval (optional)

If an administrator has enabled the [submission approval workflow](../admin-user-guide/approval-process.md) and a policy covers your schema and source, your submission is **accepted and stored exactly as usual** (same 201/200 response with `id` + `warnings`) but is held as `Pending` — it stays out of the OData feed, Explore, reports and webhooks until a reviewer approves it. No client change is needed.

- **Source.** Submissions are tagged with a source so policies can target manual vs. API traffic. Direct API calls default to `Api`; the admin console tags its own writes as `Manual`. You can override with the optional `X-Ingest-Source: api|manual` header, but you normally shouldn't.
- **Re-submitting.** Re-sending data for a window that already has a submission replaces it and, when approval applies, resets it to `Pending` — even if the previous one was approved. Editing an approved submission therefore removes it from reporting until it's approved again.
- **Approving** is an admin/approver action and is not part of the service-facing API.

---

## Drafts (optional)

A **draft** is a work-in-progress submission you can save and come back to later without it counting as a real submission — useful when a report can't be completed in one call (you're waiting on a number, or several people fill in different parts). Drafts work the same whether or not the approval workflow is enabled.

- **Saving.** Pass `?draft=true` on `POST /api/submissions` (new draft) or `PUT /api/submissions/{id}` (re-save an existing draft). Validation is **relaxed**: each value you *did* send must still be the right type and within its declared `min`/`max`, length and `regexPattern`, but required values may be missing, the one-per-cadence-window check is skipped, and the conditional-display (`Enabled if` / `Visible if`), per-value, schema-level and warning rules don't run.
- **Out of reporting.** A draft is held out of the OData feed, Explore and `/api/me/status` — and out of the accepted/pending webhooks — exactly like a submission [awaiting approval](../admin-user-guide/approval-process.md#the-lifecycle). The approval policy isn't resolved until you publish.
- **Publishing.** Replace the draft with `?draft=false` (the default) — `PUT /api/submissions/{id}` without the flag publishes it. The full validation pipeline runs, and on success it becomes a normal submission (entering approval if a policy applies).
- **No going back.** Once a submission is published it **cannot** be returned to draft — a `draft=true` replacement of a published submission is rejected with a 400. Clone it into a fresh draft instead (an admin-console action; see [submissions.md § Cloning into a new submission](../admin-user-guide/submissions.md#cloning-into-a-new-submission)).
- **Telling drafts apart.** `GET /api/submissions/{id}` carries an `isDraft` flag, and `GET /api/submissions?draft=true|false` filters the listing.

The full operator-facing walkthrough is in [submissions.md § Saving a draft](../admin-user-guide/submissions.md#saving-a-draft).

---

## Reports

Reports are HTML+Liquid templates uploaded by admins and rendered server-side. The list/get/render endpoints require the `reports:read` capability (creating/deleting report definitions needs `reports:manage`); a pure **Service** account has neither and can keep ignoring `/api/reports/*` entirely — they're a back-office feature. Full author/viewer documentation lives in [../admin-user-guide/reports.md](../admin-user-guide/reports.md).

### `GET /api/reports`

Paged listing.

```json
{
  "items": [
    {
      "id": "8e6f…",
      "name": "tonnes_summary",
      "label": "Tonnes summary",
      "description": "Per-period aggregate of tonnage submissions.",
      "type": "Aggregate",
      "targetSchemaNames": ["garbage_collection"],
      "createdAt": "2026-05-10T08:23:11Z",
      "createdBy": "admin",
      "modifiedAt": "2026-05-10T08:23:11Z",
      "modifiedBy": "admin"
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

### `GET /api/reports/{name}`

Fetch a single report's metadata. The template body is intentionally **not** part of this response — render the report to get its HTML.

### `POST /api/reports/{name}/render`

Render a report against the data envelope its `type` requires. Body fields are all optional:

```json
{
  "schemaName": "garbage_collection",
  "submissionId": "5a3c…",
  "from": "2026-05-01T00:00:00Z",
  "to":   "2026-05-31T23:59:59Z"
}
```

- `submissionId` is **required** for `Single` reports.
- `schemaName` is **required** when the report targets more than one schema (and for global reports). For single-target reports the only candidate wins.
- `from` defaults to the start of the current calendar month; `to` defaults to "now".

Response (200):

```json
{
  "html": "<!doctype html>…",
  "reportName": "tonnes_summary",
  "reportLabel": "Tonnes summary",
  "type": "Aggregate",
  "schemaName": "garbage_collection",
  "submissionId": null,
  "from": "2026-05-01T00:00:00Z",
  "to":   "2026-05-31T23:59:59Z"
}
```

`html` is the rendered output — drop it into a sandboxed iframe via `srcdoc` for safe display.

### `POST /api/admin/reports` *(requires `reports:manage`)*

`multipart/form-data` with a single `file` field. Use this from a file picker.

### `POST /api/admin/reports/json` *(requires `reports:manage`)*

JSON variant for non-browser tooling:

```json
{
  "fileName": "tonnes_summary.html",
  "content":  "---\nname: tonnes_summary\n…\n---\n<h1>…</h1>"
}
```

### `DELETE /api/admin/reports/{id}` *(requires `reports:manage`)*

Soft-delete. Idempotent.

---

## Common client patterns

### Verify before doing real work

```bash
curl -s -o /dev/null -w "%{http_code}\n" \
  -H "X-Api-Key: $INGEST_KEY" \
  https://ingest.example.org/api/me
# 200 → key is good, you are ready to submit
```

### Submit + retry on validation error

```python
import os, requests

resp = requests.post(
    "https://ingest.example.org/api/submissions",
    headers={"X-Api-Key": os.environ["INGEST_KEY"]},
    json={"samples": payload},
    timeout=10,
)

if resp.status_code == 400:
    for err in resp.json().get("errors", []):
        log.error("ingest validation: %s", err)
    raise SystemExit(2)

resp.raise_for_status()
print("created", resp.json()["id"])
```

### Validate in CI without writing anything

Use `POST /api/submissions/validate` to fail your build *before* you ever send real data. Add `?omit=cadence` so a fixture you replay every run doesn't trip the one-per-window check — you're testing the payload's shape and rules, not the live calendar.

```python
import os, requests

resp = requests.post(
    "https://ingest.example.org/api/submissions/validate?omit=cadence",
    headers={"X-Api-Key": os.environ["INGEST_KEY"]},
    json={"samples": payload},
    timeout=10,
)
resp.raise_for_status()           # 4xx only if the request itself was malformed
verdict = resp.json()

if not verdict["valid"]:
    for err in verdict["errors"]:
        print("ingest validation:", err)
    raise SystemExit(2)

for warn in verdict["warnings"]:
    print("ingest warning:", warn)
```

### Confirm the bucket is satisfied

After a submission, poll `/api/me/status` to verify the relevant value is now `satisfied: true`. This is most useful in pipelines that want to assert "yes, this period is closed out" before continuing.

### Re-key on rotation

Run two API keys in parallel during rotation: the new one rolls out to clients while the old one stays alive. Once every client reports successful auth with the new key, revoke the old one. Both keys live as separate `ApiKey` rows on the same account.

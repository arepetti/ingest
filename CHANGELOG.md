# Changelog

Notable changes per release. Versions are newest-first. No breaking changes have been released so far.

## 0.5.0

### Added

- Calculated schema values: a value can be declared with `kind: Calculated` and an `expression` that derives its result from sibling values in the same submission.
- `average(...)` built-in in expressions.
- Expression editor in the schema editor: CodeMirror-based syntax highlighting, autocomplete, and error squiggles (loaded on demand).
- Admins can delete (not just revoke) an API Key.
- Explore anomaly detection: a **Highlight anomalies** toggle on the Trend chart.
- Explore **Anomalies** tab: a cross-schema board flagging values that deviate from their own recent history.
- Explore series API: `anomaly` / `anomalyWindow` / `anomalyThreshold` / `anomalyRobust` parameters, plus the new `GET /api/admin/explore/anomalies` endpoint.

### Documentation

- Admin guide: calculated values section.
- Admin guide: Explore anomaly detection and the Anomalies tab.
- Client API reference: `kind` / `expression` on schema values.
- Power BI: incremental-refresh recipe for the samples feed (partition on the immutable `SubmittedAt`, `ModifiedAt` change-detection folding caveat, and the retroactive-edit pitfall), with a how-to note in the `ingest-samples` example.

## 0.4.0

### Added

- API keys can carry an optional description.
- Per-operator service scope: a back-office account (Operator/Approver) can be confined to a chosen subset of services.
- Submission validate endpoints (`POST /api/submissions/validate` and `.../{id}/validate`): run the full submission pipeline — validation, cadence, approval preview — without saving anything, so API clients can dry-run a payload (e.g. in CI).
- Validation rules can now compare a submission against the service's own history: `latest("value")` returns the most recent live value and `previous("value")` returns the value from the immediately preceding cadence period.
- Microsoft Teams integration: a bot prompts a user or channel for outstanding required values.
- Approval rules (in Settings): require approval per service and per schema, on top of each schema's own policy.
- Configuration backup.
- Explore "compare with previous".
- OData scorecard feed (`/odata/scorecard(mode,period)`).
- OData schemas feed (`/odata/schemas`).
- Target bands (RAG) per schema value.
- `SubmittedAt` (when a submission was reported, distinct from the measurement timestamp) is now exposed on the OData `samples` feed and the admin query endpoint.
- Draft submissions (and notifications).
- Clone into new .
- Explore view presets.
- Accounts bulk export/import: a portable, key-free JSON of all accounts.

### Changed

- Bulk-imported submissions are now dated to their data: each submission's submitted-at is set to its first sample's timestamp (rather than the import time), so back-filled history sorts and filters by when it was measured.
- Bulk submission import is now idempotent: submissions that already exist are skipped instead of failing, so re-running the same file is safe. The import report is also simpler — it shows how many succeeded and skipped, and lists only the failures.

### Fixed

- The submissions "Not required" approval filter now includes legacy submissions that predate the approval workflow.

### Documentation

- More integration examples for MHR iTrust.
- Example test data (`examples/test-data/submissions.json`): two years of weekly workforce snapshots for seeding a demo deployment.

## 0.3.0

### Added

- Optional submission approval workflow (and notifications/webhooks).
- Capability-based permissions: fine-grained per-account capabilities.
- Explore page: lightweight in-app analytics for numeric KPIs.
- Data export: download lists and reports as CSV, with reusable period filters.
- Outbound webhooks so external systems can react to events.
- Schema version history: track and review how schemas change over time.

### Changed

- Authorisation now resolves per-capability instead of by role. Existing accounts keep their previous access via role-default capability bundles — no migration or config change required.

## 0.2.0

### Added

- Single sign-on (SSO) for signing in to the admin console.
- Audit log of administrative actions, browsable from the console.
- Email addresses on accounts, plus SMTP-based email notifications.
- Bulk import of submissions.
- Backup and restore of system data.
- Expiration dates for API keys.
- GDPR support: per-subject data export and erasure.
- Recording of validation warnings, so unusual-but-valid values are retained and surfaced.
- Dedicated analytics for missing submissions, including dashboard widgets.

### Changed

- Reworked and polished the admin console UI.
- Improved accessibility across the admin console.
- Added safeguards that prevent accidental destructive edits to submitted data.

## 0.1.0

### Added

Initial release. Core capabilities:

- KPI schema catalogue: typed values with units, per-value reporting cadences, and required flags.
- Server-side validation: type/range/regex checks, custom rule expressions, conditional fields, soft warnings, and cadence (one-submission-per-window) enforcement.
- Data submission via a REST API or the bundled admin web console, including entry on behalf of a service.
- Admin SPA (Fluent UI) for managing services, schemas, and submissions, with per-service status tracking.
- API-key authentication with Service / Operator / Admin roles, plus key rotation and revocation.
- OData v4 feed for Power BI and other reporting tools.
- HTML + Liquid report templates.
- Ships as a single Docker image (API + admin SPA) backed by MongoDB.


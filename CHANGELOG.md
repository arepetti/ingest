# Changelog

Notable changes per release. Versions are newest-first. No breaking changes have been released so far.

## 0.4.0

### Added

- Validation rules can now compare a submission against the service's own history: `latest("value")` returns the most recent live value and `previous("value")` returns the value from the immediately preceding cadence period.
- Microsoft Teams integration: a bot prompts a user or channel for outstanding required values, on a flexible schedule (daily, weekly, monthly, quarterly, semi-annually, or yearly) or on demand.
- Approval rules (in Settings): require approval per service and per schema, on top of each schema's own policy.
- Configuration backup (in Tools): export and restore all Settings-page configuration for copying configuration between environments or recovering it after a disaster.
- Explore "compare with previous": overlay the Trend chart with the same selection shifted back by 1 month, 6 months, or 1 year to read this period against an earlier one.
- Explore view presets: save up to five named filter/view selections in the browser and reload them from a dropdown.
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


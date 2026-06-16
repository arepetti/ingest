# Changelog

Notable changes per release. Versions are newest-first. No breaking changes have been released so far.

## 0.3.0

### Added

- Explore page: lightweight in-app analytics for numeric KPIs (trend, service comparison, and latest-value snapshot), with an optional trend projection, CSV/PNG export, and shareable filter links. A convenience for deployments without a BI tool; Power BI remains the primary analytics surface.
- Data export: download lists and reports as CSV, with reusable period filters.
- Outbound webhooks so external systems can react to events.
- Schema version history: track and review how schemas change over time.

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


# Data protection (EU GDPR)

This page explains the **data-protection features Ingest provides in the product** and where each one lives. It is a description of the tooling, not legal advice — the legal basis, privacy notice, and decision to act on any given request remain the controller's responsibility (see [Out of scope](#what-ingest-does-not-do)).

Article references are to **Regulation (EU) 2016/679 (GDPR)**. The **UK GDPR** and the **Data Protection Act 2018** keep the *same article numbering*, so everything below applies identically under either regime.

The data-subject actions here are gated by the `privacy:read` (export/access) and `privacy:manage` (erasure, retention) capabilities — both in the Admin default bundle, and grantable to a non-admin such as a data-protection officer.

## What personal data the system holds

Ingest is a KPI-ingestion backend, not a person-tracking system, but a handful of fields are personal data:

| Where | Field(s) |
|-------|----------|
| Accounts | contact `email`, `label`/`name`, SSO identity links (provider + verified email) |
| API keys | metadata only (no secret); tied to an account |
| Submissions / samples | free-text string KPI values and per-sample notes can contain personal data; the denormalised `ServiceName` snapshot |
| Email outbox | full rendered subject/body of every message sent, plus the recipient address |
| Audit log | actor/target **names** captured at the time of each change |

The numeric/date/boolean KPI values themselves are statistical, not personal.

## What Ingest does

Three capabilities cover the data-subject rights and storage-limitation duty that need code:

1. [**Erasure**](#1-erasure-right-to-erasure-art-17) — remove or anonymise everything tied to a subject.
2. [**Retention purge**](#2-retention-purge-storage-limitation-art-51e) — automatically delete data once it outlives a configured window.
3. [**Personal-data export (DSAR)**](#3-personal-data-export-right-of-access-art-15) — download everything held about a subject.

### 1. Erasure (right to erasure, Art. 17)

**Where:** Accounts page → row menu (or the detail-drawer toolbar) → **Erase (GDPR)**.
**API:** `POST /api/admin/accounts/{id}/erase` with body `{ "mode": "Anonymise" | "Delete" }` (Admin).

You choose one of two modes in the dialog. **Both are irreversible** and both bypass the ordinary "account has submitted data" delete guard, so the dialog requires you to tick *I understand this cannot be undone* first.

- **Anonymise** — keeps the *statistical* record but strips the identity:
  - The account is pseudonymised (`erased-…`); label, description, email and SSO links are cleared and the account is disabled.
  - API keys and outbox emails for the subject are deleted.
  - Free-text in submissions and samples (string KPI values, notes, warnings) is redacted, while numeric/date/boolean values, the owning id and timestamps are kept so historical dashboards still add up. The `ServiceName` snapshot is set to the pseudonym.
  - Audit-log actor/target names for the subject are rewritten to the pseudonym — the accountability trail survives without the identity.
- **Delete** — permanently removes the account and **everything** tied to it: keys, submissions, samples, outbox emails, audit entries and notification-dedupe markers.

Either way, a single audit entry is written for the erasure action itself (who, when, which mode), naming only the pseudonym — so the action is accountable under Art. 5(2) without re-introducing the identity.

> Choose **Anonymise** when you must remove the person but still need the KPI numbers; choose **Delete** when nothing about the subject should remain.

### 2. Retention purge (storage limitation, Art. 5(1)(e))

**Where:** configuration only — it's a deployment policy, not a day-to-day screen. See [admin-user-guide/settings.md § Retention](admin-user-guide/settings.md#retention) and [setup/configuration.md](setup/configuration.md).
**API (manual run):** `POST /api/admin/retention/run` (Admin) — works whether or not the background worker is enabled, and returns a per-target count of what it removed.

A background `RetentionWorker` (registered only when `Retention:Enabled`) periodically hard-deletes data that has outlived its window. Each window is a day count where `0` (or absent) means **keep forever**:

| Key | Purges |
|-----|--------|
| `Retention:SentEmailsDays` | delivered/failed outbox emails older than N days (highest-value — unbounded full-body PII) |
| `Retention:SoftDeletedDays` | soft-deleted accounts/schemas/submissions/samples/reports whose `DeletedAt` is older than N days (the fix for soft-delete lingering forever) |
| `Retention:AuditLogDays` | audit entries older than N days |
| `Retention:NotificationLogDays` | notification dedupe markers older than N days |

Off by default, so turning the feature on with no day-counts set still purges nothing until you choose what to expire. (A purge job is used rather than Mongo TTL indexes because the soft-delete rule needs an `IsDeleted` filter and the job is portable across Cosmos vCore / Atlas / self-hosted.)

### 3. Personal-data export (right of access, Art. 15)

**Where:** Accounts page → row menu (or the detail-drawer toolbar) → **Export personal data**.
**API:** `GET /api/admin/accounts/{id}/personal-data/export` (Admin) — returns a downloadable JSON file.

The bundle gathers everything tied to the subject into one file: the account record (labels, contact email, SSO links), **API-key metadata (never the secrets)**, every submission and sample they own, the **outbox emails sent to them** (which the registry backup deliberately omits), and the audit-log entries where they appear as actor or target. Hand this file to the subject to satisfy an access request.

## Supporting safeguards already in place

These aren't GDPR features per se but support the same goals:

- **API keys** are stored only as a salted HMAC-SHA256 hash; the plaintext is shown once and never recoverable. Keys can be given an **optional expiry** (up to two years) and revoked at any time — see [admin-user-guide/accounts.md § Issuing and rotating keys](admin-user-guide/accounts.md#issuing-and-rotating-keys).
- **Least-privilege capabilities** gate who can read or change what (roles seed them, then they're tuned per account) — see [architecture/authentication.md § Authorisation: capabilities](architecture/authentication.md#authorisation-capabilities).
- **Audit log** records every create/edit/delete with actor and timestamp.
- **Transport security, rate limiting and IP allow-listing** are delegated to the hosting layer — see [setup/hosting.md § Network controls](setup/hosting.md#network-controls).

## What Ingest does *not* do

Intentionally out of scope of the product — these are controller/deployment responsibilities:

- **Governance artifacts** — DPIA, privacy notice, and Records of Processing Activities (ROPA) are documents you maintain, not features.
- **Lawful-basis tracking / consent management** — Ingest assumes a public-task or legitimate-interest basis appropriate to organisational KPI or survey reporting; it does not record per-subject consent.
- **App-level HTTPS/HSTS enforcement** — handled at the ingress/reverse proxy ([setup/hosting.md](setup/hosting.md)).
- **Automated detection of personal data inside free-text KPI fields** — anonymise redacts *all* string values/notes for an erased subject, but the system can't tell which free-text elsewhere happens to be personal. Schema authors should avoid collecting personal data in free-text where it isn't needed.
- **Special-category data (Art. 9)** — there is no dedicated handling; don't collect it through KPI fields.

## Quick reference

| Right / duty | Article | Feature | UI | API |
|--------------|---------|---------|----|-----|
| Erasure | 17 | Anonymise / Delete | Accounts → Erase (GDPR) | `POST /api/admin/accounts/{id}/erase` |
| Access (DSAR) | 15 | Per-subject export | Accounts → Export personal data | `GET /api/admin/accounts/{id}/personal-data/export` |
| Storage limitation | 5(1)(e) | Retention purge | *(config)* | `POST /api/admin/retention/run` |
| Accountability | 5(2) | Audit log + erasure audit entry | Audit page | `GET /api/admin/audit` |

## See also

- [admin-user-guide/accounts.md § Data-subject rights (GDPR)](admin-user-guide/accounts.md#data-subject-rights-gdpr) — step-by-step for erase and export.
- [admin-user-guide/settings.md § Retention](admin-user-guide/settings.md#retention) — retention configuration.
- [setup/configuration.md](setup/configuration.md) — every `Retention:*` key.
- [architecture/authentication.md](architecture/authentication.md) — key lifecycle, roles, and threat model.

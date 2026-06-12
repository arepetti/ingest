# Settings

**Settings** is an admin-only hub (it doesn't appear for operators or services). It's organised into tabs. When the **email feature is enabled** (`Email:Enabled`, on by default — see [setup/configuration.md → Email & notifications](../setup/configuration.md#email--notifications)) you get **Email**, **Email templates** and **Notifications** tabs. **Backup & restore** is always present.

> When `Email:Enabled` is `false`, the three email/notification tabs disappear, along with the **Audit → Sent emails** tab and the per-account **Send email** action. Everything below the Backup section then simply doesn't apply.

## Email (SMTP)

The **Email** tab holds the SMTP connection used for every outgoing message. It's stored in the database (not config), so you can change it any time without a redeploy. A badge shows whether it's **Configured** (host + from-address present) or **Not configured**.

Fields:

- **Host** / **Port** — your SMTP server. 587 with **Use TLS (STARTTLS)** is the usual combination.
- **From address** / **From name** — stamped on every message. The from-address is validated.
- **Username** — leave blank for an anonymous relay.
- **Password** — *write-only*. The stored value is **never shown again**; the UI only tells you whether one is set. Tick **Set / change password** to replace it (a blank value clears it). It's encrypted at rest.

If the SMTP settings are missing or wrong, queued emails don't vanish — they're marked **Failed** with a clear reason on the **Audit → Sent emails** tab, so you can fix the settings and re-send.

> **Seeding from configuration.** On a brand-new deployment you can pre-fill these from `Email:Smtp:*` config keys; they're used only until a settings document exists, after which the database wins. See [setup/configuration.md](../setup/configuration.md#email--notifications).

## Email templates

Notification emails are built from **Liquid templates** stored in the database. The **Email templates** tab lists the built-in templates; pick one to edit its **Subject**, **Text body** and optional **HTML body**. Liquid is validated on save, so a typo is rejected with a message rather than breaking delivery later. If you leave the HTML body blank, a plain-text email is sent.

The built-in templates and the model each one is rendered against:

| Key                       | Used for                | Model fields |
|---------------------------|-------------------------|--------------|
| `notification.upcoming`   | Upcoming reminder       | `service.name`, `service.label`, `items[]` → `schema`, `value`, `cadence`, `periodEnd` |
| `notification.missed`     | Missed alert            | `service.name`, `service.label`, `items[]` → `schema`, `missingCount`, `totalCount`, `periodStart`, `periodEnd` |
| `notification.warnings`   | Submission with warnings| `service.name`, `service.label`, `submissionId`, `submittedAt`, `warnings[]` (strings) |

Reference fields with the usual Liquid syntax, e.g. `{{ service.label }}` or `{% for item in items %}{{ item.value }}{% endfor %}`.

## Notifications

The **Notifications** tab controls *which events generate emails* and *who receives them*. There are three independent triggers:

- **Upcoming submission reminder** — a required value's cadence window is about to close and nothing has been submitted yet. Set the **lead time** (hours before the window closes) to control when it fires.
- **Missed submission alert** — a required value's *previous* window closed without a submission (the deadline passed).
- **Submission with warnings notice** — a submission was accepted but carried validation warnings.

For each trigger you choose the recipients (additive):

- **Notify the service account** — sends to the contact **email on the service account** the event is about. (Set those on [accounts.md](accounts.md).)
- **Notify the admin/operator list** — sends to the shared **recipient list** at the bottom of the tab (operator/admin accounts that have an email).

**Run now** triggers the job immediately (it also runs on a timer — `Notifications:Scheduler:PollMinutes`). Each event is **deduplicated**: the same window/submission is notified at most once, no matter how often the job runs. Enabling a trigger never floods recipients with a backlog — "upcoming" only looks at windows inside the lead time, "missed" only at the just-closed window, and "warnings" only at submissions from the last few days.

Generated emails land in the outbox and are delivered by the sender like any other; watch their status on **Audit → Sent emails**.

## Sending an ad-hoc email

Operators and admins can send a one-off plain-text email to any account that has a contact email: open the account's **⋮** menu (or its detail drawer) on the **Accounts** page and choose **Send email**. The message is queued into the outbox and delivered by the sender — success means "accepted into the queue", and you can confirm delivery on **Audit → Sent emails**.

## Backup & restore

A convenience tool to export the whole registry to one JSON file, or restore it from one.

> **This is not your primary backup.** It's meant for **small** deployments and for copying data between environments (e.g. seeding a test instance from production-like data). For real backups, take a **database-level** backup — see [setup/hosting.md → Backups](../setup/hosting.md#backups).

### Export

Click **Download backup**. Your browser downloads `ingest-backup-<timestamp>.json` containing every collection — accounts (with their hashed API keys), schemas, submissions, the derived samples read model, reports, and the audit log. Because hashed keys are included, a restored backup keeps existing API keys working.

> The whole database is loaded into memory to build the file, so this is only practical for small databases. On a large database, use a database-level backup instead.

### Restore

Click **Restore from file…**, pick a backup JSON, and confirm the warning.

- **Restore replaces all current data.** Every collection in the file is emptied and repopulated from the backup. Anything currently in the database that isn't in the file is gone.
- **It is not transactional.** If the restore fails part-way (e.g. a malformed file slips past the format check), the database can be left partially restored. Take a fresh backup before restoring.
- **The file is validated first.** A file that isn't an Ingest backup, is the wrong version, or isn't valid JSON is rejected before anything is touched.
- **Lock-out caution.** Because accounts and keys are part of the snapshot, restoring an *old* backup can revert your own account/keys to their state at backup time. Make sure you still hold a valid key (or SSO link) for an enabled admin account that exists *in the backup* before you restore.

On success the tool reports how many documents were written into each collection.

## Retention

Retention is the time-based clean-up that enforces GDPR storage limitation — it hard-deletes data once it has outlived its configured window. It is **configuration-driven, not a Settings-page screen**, because it's a deployment policy rather than day-to-day data. Set it in `appsettings.json` (or environment variables) under the `Retention` section:

| Key                            | Default | Meaning |
|--------------------------------|---------|---------|
| `Retention:Enabled`            | `false` | Master switch. When off, nothing is ever purged and the background worker isn't started. |
| `Retention:PollHours`          | `24`    | How often the in-process worker runs a purge pass (floored at 1). |
| `Retention:SentEmailsDays`     | `0`     | Days to keep delivered/failed outbox emails (full-body PII). `0` = keep forever. |
| `Retention:AuditLogDays`       | `0`     | Days to keep audit-log entries. `0` = keep forever. |
| `Retention:SoftDeletedDays`    | `0`     | Days to keep soft-deleted rows (accounts/schemas/submissions/samples/reports) before hard-deleting them. `0` = keep forever. |
| `Retention:NotificationLogDays`| `0`     | Days to keep notification dedupe markers. `0` = keep forever. |

Every window defaults to `0` (keep forever), so turning the feature on with no day-counts set does nothing until you choose what to expire. The highest-value setting is usually `SentEmailsDays` — outbox messages hold unbounded full-content PII with no other lifecycle.

You can trigger a pass on demand (for testing or an external scheduler) with `POST /api/admin/retention/run` (Admin), which works whether or not the in-process worker is enabled and returns a per-target count of what it removed.

## Where to go next

- [setup/hosting.md → Backups](../setup/hosting.md#backups) — the database-level backups that are the real safety net.
- [accounts.md](accounts.md) — manage the accounts and keys a restore brings back.

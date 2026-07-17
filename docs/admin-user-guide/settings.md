# Settings

**Settings** is the configuration hub. It appears for any account that holds at least one settings-related capability (e.g. `settings:read`, `notifications:read`, `webhooks:read`) — in practice admins, plus any non-admin you've specifically granted one of those. Each section is independently gated by its own capability, so a user only sees (and can only change) the parts they're permitted to. It uses a master-detail layout — a vertical list of **sections** on the left, the selected section's content on the right (much like VS Code's settings). The sections shown depend on which features are enabled **and** your capabilities:

- When the **approval workflow is enabled** (`Approval:Enabled`, on by default) you get an **Approval** section for the global default policy and a **Rules** section for per-service/per-schema approval rules — both documented in [approval-process.md](approval-process.md).
- When the **email feature is enabled** (`Email:Enabled`, on by default — see [setup/configuration.md → Email & notifications](../setup/configuration.md#email--notifications)) you get **Email**, **Email templates** and **Notifications**.
- When **webhooks are enabled** (`Webhooks:Enabled`, **off** by default) you get a **Webhooks** section — documented separately in [webhooks.md](webhooks.md).
- When **integrations are enabled** (`Integrations:Enabled`, **on** by default) you get an **Integrations** section and a **Teams connection** section (gated by `integrations:read` / `integrations:manage`) — the Microsoft Teams integration, documented separately in [integrations.md](integrations.md).

> When `Email:Enabled` is `false`, the three email/notification sections disappear, along with the **Audit → Sent emails** tab and the per-account **Send email** action. If none of email, webhooks, or integrations is enabled, the Settings page shows a short "nothing to configure" notice.

> **Backup & restore moved.** It isn't really a setting, so it now lives on the **Tools** page (in the sidebar, directly above Settings) — see [tools.md](tools.md).

## Rules

The **Rules** section (shown when the approval workflow is enabled, gated by the same `settings:read` / `settings:manage` capabilities as **Approval**) is a generic home for cross-cutting rules. Today the only kind is an **approval rule**: it requires approval for a chosen set of **services** and **schemas**, on top of — and independently of — each schema's own policy.

- Click **Add rule** to open the side drawer, or click a row (or its **⋮ → Edit**) to change an existing one. Each rule has an optional label, an **Enabled** switch, a **Services** selector (or **All services**), a **Schemas** selector (or **All schemas**), and an approval policy (the same editor used on schemas — **Required** with its own approvers and source scope, or **Use the global default**).
- Multiple services and schemas can be selected in one rule, and either side can be left as "All".
- The full behaviour, including how rules combine with schema and global policies and how to use an API-only rule to force manual intervention for partially automated feeds, is in [approval-process.md → Rules](approval-process.md#rules-per-service--schema).

## Email (SMTP)

The **Email** section holds the SMTP connection used for every outgoing message. It's stored in the database (not config), so you can change it any time without a redeploy. A badge shows whether it's **Configured** (host + from-address present) or **Not configured**.

Fields:

- **Host** / **Port** — your SMTP server. 587 with **Use TLS (STARTTLS)** is the usual combination.
- **From address** / **From name** — stamped on every message. The from-address is validated.
- **Username** — leave blank for an anonymous relay.
- **Password** — *write-only*. The stored value is **never shown again**; the UI only tells you whether one is set. Tick **Set / change password** to replace it (a blank value clears it). It's encrypted at rest.

If the SMTP settings are missing or wrong, queued emails don't vanish — they're marked **Failed** with a clear reason on the **Audit → Sent emails** tab, so you can fix the settings and re-send.

> **Seeding from configuration.** On a brand-new deployment you can pre-fill these from `Email:Smtp:*` config keys; they're used only until a settings document exists, after which the database wins. See [setup/configuration.md](../setup/configuration.md#email--notifications).

## Email templates

Notification emails are built from **Liquid templates** stored in the database. The **Email templates** section lists the built-in templates; pick one to edit its **Subject**, **Text body** and optional **HTML body**. Liquid is validated on save, so a typo is rejected with a message rather than breaking delivery later. If you leave the HTML body blank, a plain-text email is sent.

The built-in templates and the model each one is rendered against:

| Key                       | Used for                | Model fields |
|---------------------------|-------------------------|--------------|
| `notification.upcoming`   | Upcoming reminder       | `service.name`, `service.label`, `items[]` → `schema`, `value`, `cadence`, `periodEnd` |
| `notification.missed`     | Missed alert            | `service.name`, `service.label`, `items[]` → `schema`, `missingCount`, `totalCount`, `periodStart`, `periodEnd` |
| `notification.warnings`   | Submission with warnings| `service.name`, `service.label`, `submissionId`, `submittedAt`, `warnings[]` (strings) |
| `notification.pendingApproval` | Submission pending approval | `service.name`, `service.label`, `submissionId`, `submittedAt`, `schemas[]` (strings), `sampleCount` |
| `notification.approved`   | Submission approved     | `service.name`, `service.label`, `submissionId`, `submittedAt`, `schemas[]`, `sampleCount`, `decidedBy`, `note` |
| `notification.rejected`   | Submission rejected     | `service.name`, `service.label`, `submissionId`, `submittedAt`, `schemas[]`, `sampleCount`, `decidedBy`, `reason` |
| `notification.draftSaved` | Draft saved nudge       | `service.name`, `service.label`, `submissionId`, `submittedAt`, `schemas[]` (strings), `sampleCount`, `editPath` |

Reference fields with the usual Liquid syntax, e.g. `{{ service.label }}` or `{% for item in items %}{{ item.value }}{% endfor %}`.

> The draft nudge's `editPath` is a **relative** path (e.g. `/submissions/{id}/edit`), shown as plain text rather than a link — the server doesn't know its own public URL, so recipients paste it after their console address.

## Notifications

The **Notifications** section controls *which events generate emails* and *who receives them*. There are three independent triggers:

- **Upcoming submission reminder** — a required value's cadence window is about to close and nothing has been submitted yet. Set the **lead time** (hours before the window closes) to control when it fires.
- **Missed submission alert** — a required value's *previous* window closed without a submission (the deadline passed).
- **Submission with warnings notice** — a submission was accepted but carried validation warnings.
- **Draft saved nudge** — a submission was [saved as a draft](submissions.md#saving-a-draft). Unlike the others this is a deliberate, **un-deduplicated** nudge: it fires on *every* draft save (create or re-save) so collaborators are prompted to keep filling it in. It's event-driven (sent the moment the draft is saved, not on the timer) and independent of the approval feature — it appears whether or not approval is enabled. The email carries the relative edit path to reopen the draft. Off by default.

When the [approval workflow](approval-process.md) is enabled (`Approval:Enabled`) three more triggers appear:

- **Submission pending approval notice** — a submission is held awaiting approval. **The submission's designated approvers are always emailed when this is on** so they know there's something to review; the two recipient switches below add the submitter and/or admin-list copies on top.
- **Submission approved notice** — a pending submission was approved and is now live.
- **Submission rejected notice** — a pending submission was rejected; the email carries the reviewer's reason.

For each trigger you choose the recipients (additive):

- **Notify the service account** — sends to the contact **email on the service account** the event is about. (Set those on [accounts.md](accounts.md).)
- **Notify the admin/operator list** — sends to the shared **recipient list** at the bottom of the section (operator/admin accounts that have an email).

**Run now** triggers the job immediately (it also runs on a timer — `Notifications:Scheduler:PollMinutes`). Each event is **deduplicated**: the same window/submission is notified at most once, no matter how often the job runs. Enabling a trigger never floods recipients with a backlog — "upcoming" only looks at windows inside the lead time, "missed" only at the just-closed window, and "warnings" only at submissions from the last few days.

Unlike the three scheduled triggers above, the **approval notices and the draft nudge are event-driven**: they are sent the moment a submission is held pending, approved, rejected, or saved as a draft (not on the timer, and not affected by **Run now**). The draft nudge is also the one trigger that is **not** deduplicated — every draft save re-notifies, by design.

Generated emails land in the outbox and are delivered by the sender like any other; watch their status on **Audit → Sent emails**.

## Sending an ad-hoc email

Operators and admins can send a one-off plain-text email to any account that has a contact email: open the account's **⋮** menu (or its detail drawer) on the **Accounts** page and choose **Send email**. The message is queued into the outbox and delivered by the sender — success means "accepted into the queue", and you can confirm delivery on **Audit → Sent emails**.

## Backup & restore

Backup & restore has moved to the **Tools** page (sidebar, directly above Settings) — it's an operational utility rather than a configuration screen. See [tools.md](tools.md).

## Submission periods

The **Submission periods** section (gated by `settings:read` / `settings:manage`) controls two related but distinct things for each of the 7 cadences: where its **period** (bucket) boundaries fall, and how wide its **submission window** is around that period. A live "Current periods" table at the bottom of the section shows exactly what both resolve to right now, for every cadence.

### Period alignment

By default every cadence follows the plain calendar (fiscal year = calendar year, weeks start Monday, months start on the 1st, fortnights anchored to 2001-01-01) — the top form lets you shift those alignments to match your organisation's actual reporting calendar without changing any code:

- **Fiscal year starts in** — the month (1–12) the fiscal year begins on. This also anchors **Quarterly** and **SemiAnnually** cadences, which are computed as sub-periods of the fiscal year (e.g. a July-start fiscal year makes Q1 = Jul–Sep).
- **Week starts on** — the day a **Weekly** bucket begins.
- **Month starts on day** — the day-of-month (1–28) a **Monthly** bucket begins; a submission before that day falls into the previous month's bucket.
- **Fortnight anchor** — a reference date a **Fortnightly** bucket boundary is aligned to; every 14-day window is computed relative to it.

Changing an anchor takes effect immediately for **new** submissions, live "current period" checks, and Explore/scorecard math. It does **not** retroactively re-bucket samples already projected under the old alignment — those keep the `PeriodStart`/`PeriodEnd` they were written with. Plan changes for a period boundary (e.g. right after a fiscal year closes) to avoid confusing in-flight cadences.

### Submission windows

A period's boundaries and the actual window during which a service may act on it are two different things. The **Submission windows** table lets you set, per cadence:

- **Open offset (hours)** — how long *after* the period starts before a service may create a submission for it. `0` (the default) means the window opens exactly when the period does.
- **Grace period (hours)** — how long *after* the period ends a service may still create or edit a submission for it. `0` (the default) means the window closes exactly when the period does — the historical behaviour.

With everything at the default of 0, a service can only create/edit a submission whose sample falls in the cadence's *current* period — this is unchanged from before this feature existed. A non-zero open offset delays that window's start; a non-zero grace extends how long a period stays editable after it closes (handy for a Monthly report that's due a few days into the following month, or a Yearly one with a month of slack). Outside the window, service create/replace is rejected with a 403 explaining whether the period "doesn't open until…" or "closed on…"; **drafts are exempt** (a draft is never live, so it carries no deadline), and **admins are always exempt** (admin create/replace remain the remediation path for backdating or correcting a submission after the window has closed). "Missed submission" reporting and reminder emails also wait for the configured grace to elapse before flagging a period as overdue.

Like the anchors above, window changes apply going forward only and take effect immediately.

## Ingestion

The **Ingestion** section (gated by `settings:read` / `settings:manage`) is the "stop the bleeding" kill switch for a bad deployment, corrupt feed, or other incident where you need to halt incoming data without taking the whole application down.

Turning **Close all submissions** on immediately rejects (HTTP 503) any *service-facing* write:

- Service accounts calling `POST /api/submissions` / `PUT /api/submissions/{id}` (including draft saves).
- Bulk import (`Tools → Bulk import`).
- Inbound Microsoft Teams submissions.

Everything else keeps working: reads, OData/Power BI feeds, admin-driven create/replace (for remediation), schema and settings management, and login. The optional **Message** is shown in a warning banner across the top of the app for every signed-in user, and in the body of the 503 response and Teams card that services/integrations see, so people know why writes are failing without having to ask.

Remember to turn it back off once the incident is resolved — it has no automatic expiry.

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

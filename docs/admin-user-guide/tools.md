# Tools

**Tools** is a page of operational utilities — things you *do* occasionally rather than *configure* — gated by the `backup:read` capability (running a restore needs `backup:manage`). Both are in the Admin default bundle. It sits in the sidebar directly above **Settings** and uses the same master-detail layout (a list of tools on the left, the selected one on the right), with the tools grouped under **Backup & restore**. It hosts two tools: **Data backup** (the registry) and **Configuration backup** (the Settings-page configuration). More maintenance utilities will slot in as additional sections over time.

> **The `backup:read` / `backup:manage` capabilities govern both tools.** Anyone who can export or restore the data backup can also export or restore the configuration backup (which includes encrypted secrets). There is no separate permission for configuration.

## Data backup

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

## Configuration backup

A separate tool that exports just the **configuration** you manage on the **Settings** page — distinct from the data backup above. Use it to copy configuration between environments, or to recover settings after a disaster without touching the registry data.

It covers eight collections:

- **Approvals** — the default approval policy (`approvalSettings`) and the per-service/per-schema approval rules (`approvalRules`).
- **Notifications** — the SMTP settings (`emailSettings`), the editable email templates (`emailTemplates`), and the notification rules (`notificationSettings`).
- **Integrations** — the webhook endpoints (`webhookEndpoints`), the integrations (`integrations`), and the Microsoft Teams connection (`teamsConnectionSettings`).

> Retention and other settings that live only in the server's configuration file are **not** included — they aren't stored in the database. Reapply them through configuration.

### Export

Click **Download configuration**. Your browser downloads `ingest-config-<timestamp>.json`.

> **Secrets are included as ciphertext.** The SMTP password, webhook signing secrets, and the Teams bot secret are exported exactly as stored — encrypted. They are encrypted with a key derived from `ApiKey:Pepper`, so they only decrypt (and therefore only work) on a deployment configured with the **same** `ApiKey:Pepper`. Restoring onto a server with a different pepper restores everything else correctly, but you'll need to re-enter those secrets afterwards. See [setup/configuration.md](../setup/configuration.md) for the pepper.

### Restore

Click **Restore from file…**, pick a configuration JSON, and confirm the warning.

- **Restore replaces all current configuration.** Every configuration collection in the file is emptied and repopulated. As with the data backup, it is not transactional and the file is validated first.
- **Secrets are preserved when omitted.** If an incoming document doesn't carry its secret (for example a file you hand-edited to drop the ciphertext), the existing stored secret is kept rather than wiped — so a config-only file never silently clears a working SMTP password or bot secret.

On success the tool reports how many documents were written into each collection.

## Where to go next

- [setup/hosting.md → Backups](../setup/hosting.md#backups) — the database-level backups that are the real safety net.
- [setup/disaster-recovery.md](../setup/disaster-recovery.md) — recovering an instance, including configuration.
- [settings.md](settings.md) — the admin configuration hub (approvals, email, notifications, webhooks, integrations).

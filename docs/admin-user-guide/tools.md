# Tools

**Tools** is a page of operational utilities — things you *do* occasionally rather than *configure* — gated by the `backup:read` capability (running a restore needs `backup:manage`). Both are in the Admin default bundle. It sits in the sidebar directly above **Settings** and uses the same master-detail layout (a list of tools on the left, the selected one on the right). Today it hosts a single tool, **Backup & restore**; more maintenance utilities will slot in as additional sections over time.

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

## Where to go next

- [setup/hosting.md → Backups](../setup/hosting.md#backups) — the database-level backups that are the real safety net.
- [settings.md](settings.md) — the admin configuration hub (email, notifications, webhooks).

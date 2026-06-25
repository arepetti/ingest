# Accounts

Open **Accounts** in the sidebar. The grid shows every account — interactive users and application credentials, services and admins alike — with a coloured avatar (gray = disabled; otherwise colour-coded by role/kind) and the **Actions** menu on the right.

Two orthogonal classifications drive everything you'll do here:

- **Kind** — `User` (can sign in to the SPA) or `Application` (API-only, never logs in).
- **Role** — `Service`, `Approver`, `Operator`, or `Admin`. Roles are now **templates**: picking one only seeds a default bundle of *capabilities*, which you can then tune freely (see [Permissions (capabilities)](#permissions-capabilities) below). See [architecture/authentication.md § Authorisation: capabilities](../architecture/authentication.md#authorisation-capabilities) for the full model. The `Approver` role is the reviewer for the optional [submission approval workflow](approval-process.md).

## Creating an account

1. Click **New account** at the top right.
2. Fill in:
   - **Name** — a short, stable machine-style identifier (e.g. `roads-team`, `analytics-jane`). Must be unique across all accounts ever created (including soft-deleted ones).
   - **Label** — the friendly name to display in the UI (e.g. *Roads & Highways team*, *Jane (Analytics)*).
   - **Description** — free-form notes.
   - **Email** — contact address used by the email and notification features. **Required when creating a new account.** (Validated for shape; stored lower-cased.)
   - **Kind** — `Application` for automation credentials, `User` for people who will sign in to the SPA.
   - **Role** — `Service`, `Approver`, `Operator`, or `Admin`.
   - **Enabled** — leave on unless you want to pre-create a disabled account.
3. Click **Save**.

The new row appears at the top of the grid. Newly-created accounts have **no API key yet** — see "Issuing and rotating keys" below.

## Editing an account

Use the row menu's **Edit** action. Name and Kind are **immutable** — to change either you must delete the account and create a fresh one. Label, Description, Email, Role, and Enabled are all editable.

Email is required when *creating* an account, but accounts that predate this field (or were created without one) can still be saved with an empty email when editing — fill it in whenever you're ready. A non-empty value must be a valid address.

Toggling **Enabled** off immediately invalidates every key for this account; toggling it back on re-enables the existing keys.

## Permissions (capabilities)

Below the role selector the editor shows a **Permissions** panel — a grouped checklist of every fine-grained capability the system understands (schemas, submissions, reports, accounts, API keys, audit, webhooks, notifications, privacy, backup and settings, each with a `read` and, where it applies, a `manage`/verb capability). This is what actually governs what the account can do and see; the role is just the template that pre-fills it.

- **Picking a role pre-fills the checklist** with that role's default bundle. `Operator` ticks the read-everything boxes; `Approver` ticks *view + approve submissions*; `Service` ticks nothing (a pure submitter); `Admin` holds everything.
- **You can tick or untick any box** on a non-admin account. This is the whole point of the model: grant one trusted operator `schemas:manage` without making them an admin, or a service-desk user just `accounts:read` + `apikeys:manage`, and nothing else.
- **Admins always hold every capability.** The checklist is shown read-only (all ticked) for `Admin` accounts — the role is the lockout-safe floor and can't be reduced.
- **Leaving the checklist exactly on the role default** stores *no override*, so the account keeps tracking the role's defaults if those ever change. Deviating from the default stores your explicit selection.

Capabilities take effect the next time the account authenticates (a fresh API request, or the next page load for a signed-in SPA user). In the SPA, the sidebar, dashboard cards and action buttons a user sees are driven entirely by their effective capabilities — someone without `schemas:manage` simply won't see the schema-editing controls.

## Service scope (limiting an operator to a subset of services)

Capabilities decide *what kinds of thing* an account can do; the **Service scope** decides *which services' data* it can do them to. By default a back-office account (an `Operator` or `Approver`) is **unrestricted** — it sees every service, exactly as before. You can instead confine it to a chosen subset.

In the account editor, back-office roles show a **Service scope** picker below the Permissions panel:

- **Leave it empty** for the default, unrestricted access — the account sees every service.
- **Pick one or more services** to confine the account to just those. Every cross-service read it makes — the submissions list, single-submission lookup, the review/approval queue, Explore, the status and missing-data reports, the OData/Power BI feed and the ad-hoc query — is filtered to the chosen services, and any other service is invisible (out-of-scope submissions read back as *not found*). Attempting to *write* on behalf of an out-of-scope service (e.g. creating or importing a submission for it) is refused.

Notes and rules:

- **The picker only appears for `Operator` and `Approver` accounts.** `Service` accounts only ever see their own data, and `Admin` accounts are deliberately exempt — an admin always sees every service, even if a scope was somehow stored against the account.
- **Assigned services must be real `Service` accounts.** The editor only lists services; the server rejects an attempt to assign a non-service id.
- **A scoped person sees a "Limited view" badge** in the top bar of the SPA, with the assigned services on hover, so they always know they're looking at a subset rather than the whole estate.
- Like capabilities, a scope change takes effect the next time the account authenticates (a fresh API request, or the next SPA page load).

This is how you stand up, say, a regional operator who can review and approve only their own department's submissions without ever seeing another department's data. See [architecture/authentication.md § Authorisation: capabilities](../architecture/authentication.md#authorisation-capabilities) for how the scope is carried on the wire.

## Linking an SSO identity (only when SSO is enabled)

> This section applies **only when single sign-on is enabled** for the deployment (`Sso:EnableSso=true` with at least one configured provider — see [architecture/authentication.md § Single sign-on](../architecture/authentication.md#single-sign-on-optional-second-scheme)). **When SSO is disabled (the default), the SSO sign-in field below does not appear** in the account editor and there is nothing to configure here.

When SSO is enabled, the account editor shows an **SSO sign-in** section for **User**-kind accounts. This is how you "add a user with SSO": create (or edit) a `User` account, then link the identity they'll sign in with.

1. Edit (or create) a **User**-kind account. The **SSO sign-in** section appears below *Enabled*. (It is hidden for `Application` accounts — only `User` accounts can sign in interactively.)
2. Click **Add SSO link**, pick the **provider** (e.g. Microsoft or Google) and type the user's **verified email** at that provider (e.g. their work email).
3. **Save.** From now on that person can click **Continue with {provider}** on the login screen and be signed in *as this account* — inheriting this account's **role**.

Notes and rules:

- **Pre-provisioning only.** SSO never creates accounts. An identity that isn't linked to a live, enabled `User` account is rejected at sign-in.
- **The role comes from the linked account.** There is no group-to-role mapping; whatever role you give the account is what the SSO user gets.
- **Uniqueness.** A given `(provider, email)` pair can be linked to only one account; the server rejects a duplicate with a clear message.
- **User-kind only.** Trying to add a link to an `Application` account is rejected.
- **Revoking SSO access.** Remove the link (the **🗑** button next to the row, then *Save*) or disable/delete the account. Either immediately stops that identity from signing in.
- API keys on the same account keep working independently — linking SSO doesn't change anything about keys.

## Viewing the read-only details

Clicking a row opens a side drawer with a read-only summary, including audit info (who created/modified it, when). The drawer also has a toolbar replicating the row menu actions so you can edit or manage keys without closing the drawer first.

## Sending an email to an account

> Only when the email feature is enabled (`Email:Enabled`, the default) and the account has a contact email.

The row menu (and the detail-drawer toolbar) has a **Send email** action — available to operators and admins — for a one-off plain-text message to the account's contact email. Type a subject and body and send; the message is queued and delivered by the email sender. Confirm delivery on **Audit → Sent emails**. The same contact email is what the [notification triggers](settings.md#notifications) use when "Notify the service account" is on, so keeping it filled in is worthwhile.

## Issuing and rotating keys

Row menu → **Manage keys**. The drawer lists every key attached to the account (including revoked ones — they're shown grayed out), with a **Description** column for the free-form note described below.

- **Generate key** — issues a new key and shows the plaintext **once** in a modal dialog. Copy it now; there is no way to retrieve it later. Before generating you can optionally set:
  - a **description** — a short free-form note (up to 200 characters) recording *who or why* the key exists, and
  - an **expiry** — a date up to two years from today, or blank for a key that never expires. Expired keys stop authenticating automatically — no revoke needed — and show as **Expired** in the list.
- **Edit a description** — click the pencil next to any key's description to annotate it later (handy for keys created before you started recording this). The note is purely informational and never affects authentication.
- **Revoke** (on an active key) — marks the key revoked but keeps the row, so the history of "this key existed and was retired" stays visible. Idempotent; safe to click twice.
- **Delete** (the bin icon, on any key) — permanently removes the key from the list. It works on an already-revoked key (to tidy up) and on a still-active one (which stops it working immediately, like a revoke, and then drops the row). There is no undo, so prefer **Revoke** when you want to keep the audit trail of the key. Requires the *Manage API keys* capability.

> **Tip — temporary / cover keys.** The description and expiry pair up nicely for short-lived access. When someone needs the keys to a service or a reviewer account for a fixed window (covering annual leave, a contractor engagement, an incident), generate a **separate** key with a description like *"holiday cover for Jane — reviewer"* and an **expiry** on their last day. You then have an at-a-glance record of why each key exists and who it's for, and the temporary one disappears on its own when the cover ends — no diary reminder to revoke it. Keep the permanent and temporary keys distinct so revoking or expiring one never disrupts the other.

You can have any number of active keys per account. The pattern for a zero-downtime rotation is:

1. Generate a new key.
2. Deliver it to the consumer.
3. Wait until the consumer reports successful auth with the new key.
4. Revoke the old key.

Both old and new keys authenticate during the overlap; the consumer never sees an auth failure.

## Disabling vs deleting

- **Disable** (uncheck *Enabled*) — the account row stays, every key becomes invalid, audit history is preserved. Reversible.
- **Delete** (row menu → *Delete*) — soft-delete. The account disappears from the default listing and authentication fails for any key. Recovery requires manual database surgery.

> The server refuses to delete an account that has any live submission on its name (HTTP 409, "Account '…' has submitted data and cannot be deleted. Disable it instead…"). This protects the audit trail and keeps the OData feed / status dashboard from showing orphaned rows. If you really want the account gone, hard-delete the submissions first; otherwise just disable it.

> If you delete an account and later create a new one with the same name, the tombstone is replaced automatically — the create succeeds rather than failing with an "account already exists" conflict. The new account starts with a fresh id and a fresh key set; any soft-deleted samples the old account left behind are not touched and stay excluded from queries.

## Bulk export & import

The **⋮** (More actions) menu at the top of the grid offers three ways to get accounts in and out in bulk:

- **Export this list (CSV)** — a human-readable spreadsheet of the *currently listed* accounts (name, label, kind, role, status, email, created). Good for sharing or reporting; not meant for re-import.
- **Export accounts (JSON)** — downloads `ingest-accounts-<timestamp>.json`: a portable, re-importable file with every account's name, label, description, email, kind, role, permissions, service scope, SSO links and enabled state. Needs `accounts:read`.
- **Import accounts (JSON)…** — pick an accounts JSON to create and update accounts in bulk. Needs `accounts:manage`.

Import matches each entry on its **name**: an existing account is updated in place, an unknown name is created. It's **non-destructive** — accounts missing from the file are left alone — and entries are applied independently, so one invalid account (say, an unknown capability) is skipped and reported without blocking the rest.

> **API keys are never part of the file.** Keys are stored only as irreversible hashes, so an account *created* by an import starts with **no key** — generate one for it afterwards (see "Issuing and rotating keys" above). Accounts that already existed keep their current keys. This same export/import also lives on the [Tools](tools.md#accounts) page.

## Data-subject rights (GDPR)

Two actions on the row menu (and the detail-drawer toolbar) cover the GDPR rights that need a button in the product (EU GDPR; the UK GDPR / DPA 2018 are equivalent). **Export personal data** needs the `privacy:read` capability; **Erase (GDPR)** needs `privacy:manage`. Both are in the `Admin` default bundle, but either can be granted to a non-admin (e.g. a data-protection officer). See [docs/gdpr.md](../gdpr.md) for the full data-protection overview.

### Export personal data (right of access / DSAR)

**Export personal data** downloads a single JSON file with everything the system holds about that subject: the account record (labels, contact email, any SSO links), API-key *metadata* (never the secrets), every submission and sample they own, the emails sent to them **from the outbox** (which the registry backup omits), and the audit-log entries where they are the actor or the target. Hand this file to the subject to satisfy an access request.

### Erase (right to erasure)

**Erase (GDPR)** opens a dialog where you choose one of two modes. Both are **irreversible** and both bypass the ordinary "account has submitted data" delete guard, so you must tick *I understand this cannot be undone* before the button enables.

- **Anonymise** — keeps the *statistical* record but strips the identity. The account is pseudonymised (`erased-…`), its label/description/email/SSO links are cleared and it is disabled; API keys and outbox emails are removed; free-text (string KPI values, notes, warnings) is redacted from submissions and samples while the numeric/date/boolean values stay so historical dashboards still add up; the audit trail is rewritten to the pseudonym. Use this when you must remove the person but the KPI numbers are still needed.
- **Delete** — permanently removes the account and *everything* tied to it: keys, submissions, samples, outbox emails, audit entries and notification markers.

Either way a single audit entry is recorded for the erasure itself (who, when, which mode), naming only the pseudonym — so you keep accountability without re-introducing the identity.

> Routine, time-based clean-up (purging old sent emails, soft-deleted rows, old audit entries) is configured separately under **Retention** — see [settings.md § Retention](settings.md#retention).

## Viewing a service's status

For accounts with the **Service** role, the row menu has a **View status** entry that takes you to a dashboard showing, for every schema that service can submit to, whether the current cadence period is satisfied and how many of the schema's values have arrived (`x/y` column). This is the same information `/api/me/status` returns for the service itself — see [client/api.md § `GET /api/me/status`](../client/api.md#get-apimestatus).

## Where to go next

- [schemas.md](schemas.md) — define the KPI packages services will submit against.
- [submissions.md](submissions.md) — review the data and (when needed) edit on behalf of a service.
- [../architecture/authentication.md](../architecture/authentication.md) — the wire-level contract for API keys, plus the threat model and roles in detail.

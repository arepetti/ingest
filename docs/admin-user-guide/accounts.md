# Accounts

Open **Accounts** in the sidebar. The grid shows every account — interactive users and application credentials, services and admins alike — with a coloured avatar (gray = disabled; otherwise colour-coded by role/kind) and the **Actions** menu on the right.

Two orthogonal classifications drive everything you'll do here:

- **Kind** — `User` (can sign in to the SPA) or `Application` (API-only, never logs in).
- **Role** — `Service`, `Operator`, or `Admin`. See [architecture/authentication.md § Roles](../architecture/authentication.md#roles) for what each one can do.

## Creating an account

1. Click **New account** at the top right.
2. Fill in:
   - **Name** — a short, stable machine-style identifier (e.g. `roads-team`, `analytics-jane`). Must be unique across all accounts ever created (including soft-deleted ones).
   - **Label** — the friendly name to display in the UI (e.g. *Roads & Highways team*, *Jane (Analytics)*).
   - **Description** — free-form notes.
   - **Kind** — `Application` for automation credentials, `User` for people who will sign in to the SPA.
   - **Role** — `Service`, `Operator`, or `Admin`.
   - **Enabled** — leave on unless you want to pre-create a disabled account.
3. Click **Save**.

The new row appears at the top of the grid. Newly-created accounts have **no API key yet** — see "Issuing and rotating keys" below.

## Editing an account

Use the row menu's **Edit** action. Name and Kind are **immutable** — to change either you must delete the account and create a fresh one. Label, Description, Role, and Enabled are all editable.

Toggling **Enabled** off immediately invalidates every key for this account; toggling it back on re-enables the existing keys.

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

## Issuing and rotating keys

Row menu → **Manage keys**. The drawer lists every key attached to the account (including revoked ones — they're shown grayed out).

- **Generate key** — issues a new key and shows the plaintext **once** in a modal dialog. Copy it now; there is no way to retrieve it later.
- **Revoke** (in the row menu of an individual key) — marks the key revoked. Idempotent; safe to click twice.

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

## Viewing a service's status

For accounts with the **Service** role, the row menu has a **View status** entry that takes you to a dashboard showing, for every schema that service can submit to, whether the current cadence period is satisfied and how many of the schema's values have arrived (`x/y` column). This is the same information `/api/me/status` returns for the service itself — see [client/api.md § `GET /api/me/status`](../client/api.md#get-apimestatus).

## Where to go next

- [schemas.md](schemas.md) — define the KPI packages services will submit against.
- [submissions.md](submissions.md) — review the data and (when needed) edit on behalf of a service.
- [../architecture/authentication.md](../architecture/authentication.md) — the wire-level contract for API keys, plus the threat model and roles in detail.

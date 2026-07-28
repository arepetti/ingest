# Admin user guide

This guide walks an administrator through every task they're likely to perform from the admin SPA. It assumes you already have a working deployment (see [setup/hosting.md](../setup/hosting.md)) and at least the bootstrap admin key in hand (see [architecture/authentication.md § The bootstrap admin](../architecture/authentication.md#the-bootstrap-admin)).

The guide is split into focused pages — pick whichever matches the task at hand.

| Page                                          | What's inside                                                                 |
|-----------------------------------------------|--------------------------------------------------------------------------------|
| [accounts.md](accounts.md)                    | Creating people and applications, editing them, issuing & rotating API keys, disabling vs deleting, viewing a service's status. |
| [schemas.md](schemas.md)                      | Designing schemas: per-value type/cadence flags, multi-line validation rules, conditional display (`Enabled if` / `Visible if`), warnings, historical-data view, and threaded comments on a schema or its values. |
| [submissions.md](submissions.md)              | Browsing submissions with filters, editing/creating on behalf of a service, saving work-in-progress drafts, cloning into a new submission, bulk-importing history from JSON/CSV, deleting submissions. |
| [approval-process.md](approval-process.md)    | The optional submission approval workflow: per-schema/global source-aware policies, the `submissions:approve` capability, the review queue, and the replace-and-reset rule. |
| [reports.md](reports.md)                      | Uploading HTML+Liquid report templates, what data they receive, the viewer's filter bar. |
| [explore.md](explore.md)                      | Lightweight in-app analytics: charting numeric KPIs by period and service (Trend / Compare / Snapshot). A convenience for deployments without a BI tool — PowerBI is still the primary analytics surface. |
| [events.md](events.md)                        | The admin-recorded events timeline (maintenance windows, incidents, deployments): kinds (point in time / interval / from now on), service scoping, and how they show up on the Explore chart and the OData feed. |
| [settings.md](settings.md)                    | Settings hub (gated per-section by `settings:*`/`notifications:*`/`webhooks:*` capabilities): email (SMTP) settings, editable notification templates, notification triggers & recipients, ad-hoc email send, and retention policy. |
| [webhooks.md](webhooks.md)                    | Outbound webhooks: registering signed endpoints, subscribing to submission/window events, signature verification, retries and the delivery log. |
| [integrations.md](integrations.md)            | Microsoft Teams integration: configuring the bot connection, creating integrations scoped to services/schemas, schedules, targets (user/channel), running on demand and test sends. |
| [tools.md](tools.md)                          | Operational utilities gated by the `backup:*` capabilities — currently backup & restore (a convenience tool, *not* the primary backup). |
| [validation.md](validation.md)                | Writing custom validation rules — operators, conditionals, helpers, recipes. The companion to schemas.md when you start using the rule fields. |
| [troubleshooting.md](troubleshooting.md)      | Common error messages and what they mean.                                      |

## Signing in

1. Browse to the deployment URL (`/` of the API host) — for example `https://ingest.example.org/`.
2. Paste the API key in the form. The first one you'll use is the bootstrap admin key: either the value you set in `ApiKey:BootstrapAdminKey`, or — if you left that empty — the random key printed in the server logs on first start (see [the bootstrap admin](../architecture/authentication.md#the-bootstrap-admin)).
3. Click **Sign in**.

If you paste an **Application**-kind key the login screen rejects it with a clear error: only **User**-kind credentials can sign in. (Services use their keys against the API directly, not the SPA.)

### Signing in with single sign-on (only when SSO is enabled)

If your deployment has [SSO](../architecture/authentication.md#single-sign-on-optional-second-scheme) turned on (`Sso:EnableSso=true` with at least one configured provider), the login screen also shows **Continue with Microsoft / Google** buttons above the API-key field. Click one to sign in with your organisation account instead of pasting a key.

- This works only if an administrator has **linked your verified email** to a **User**-kind account first (see [accounts.md → Linking an SSO identity](accounts.md)). If it hasn't been linked, the sign-in is rejected with a message asking you to contact an administrator.
- API keys still work exactly as before — SSO is an *additional* way in, not a replacement.
- **When SSO is disabled (the default), none of these buttons appear** and the screen is the API-key-only form described above.

Once logged in:

- The left sidebar is **driven by your capabilities**: each entry (**Dashboard**, **Schemas**, **Accounts**, **Submissions**, **Missing**, **Explore**, **Events**, **Reports**, **Audit**, **Tools**, **Settings**) appears only when you hold the matching read capability. An admin holds them all; a custom non-admin sees exactly the subset you granted.
- An account with no back-office capabilities (a typical **Service**) sees a stripped-down sidebar (Dashboard + Submissions only).
- Your friendly **label** (or **name** as a fallback) and role show at the bottom.
- **Sign out** is the icon next to your name.

If you only see a blank screen after signing in, the most likely cause is that the API key was for a soft-deleted/disabled account. Clear `localStorage` and try a fresh key.

## Accessibility

The admin SPA is built to be usable without a mouse:

- **Skip link.** Press <kbd>Tab</kbd> once after a page loads to reveal a **Skip to main content** link that jumps past the sidebar straight to the page body.
- **Keyboard-operable grids.** Data-grid rows that open a detail drawer or page on click are in the tab order and activate with <kbd>Enter</kbd> or <kbd>Space</kbd>; a visible focus ring shows where you are. The per-row **⋮** actions menu and any links/buttons inside a row keep their own focus and keyboard behaviour.
- **Landmarks & labels.** The sidebar is a labelled navigation landmark, the page body is the `main` landmark (the skip link's target), and icon-only buttons (close, expand, row actions, account menu) carry descriptive labels for screen readers.
- **Announcements.** Notices that appear after an action — a failed save, a successful import — are live regions, so screen readers announce them (errors interrupt; everything else is announced politely). Motion respects your system **reduce-motion** setting.

## First steps after install

1. **Rotate the bootstrap admin key.** This matters most when the bootstrap key came from `ApiKey:BootstrapAdminKey` — that value may be shared (e.g. with a `docker-compose.yml` or quickstart) and is only as secret as your config.
   - Go to **Accounts**, find the bootstrap admin (default name `admin`), open the row menu (`⋮`) and choose **Manage keys**.
   - Click **Generate key**, copy the plaintext shown in the dialog and save it somewhere safe (a password manager).
   - **Revoke** the old (bootstrap) key from the same dialog.
   - Sign out and sign back in with the new key to verify everything works.
2. **Set a real `ApiKey:Pepper`** in your deployment configuration (see [architecture/authentication.md § Configuration knobs](../architecture/authentication.md#configuration-knobs)). Rotating the pepper later invalidates every key in the system, so do this once, early, and back up the value alongside the database credentials.
3. **Create at least one operator account** so day-to-day analytics work doesn't require an admin key (see [accounts.md](accounts.md)).

## Dashboard

The dashboard greets you by **label** (or **name**) and surfaces a few at-a-glance widgets:

- For back-office accounts: total counts of services, schemas and submissions (each card shown only if you hold the relevant read capability), plus a **Missing submissions** section (needs `status:read`) showing one card per cadence (Daily / Weekly / Fortnightly / Monthly / Quarterly / Semi-annually / Yearly) that currently has work outstanding. Each card lists the affected `service • schema` rows with a `missing/total` count, and each row links straight to that service's status page so you can drill in. Cadences with nothing missing are simply omitted — if the whole section is gone, everyone is up to date for the current windows. Accounts with `submissions:approve` also get a **Pending approvals** card.
- For services (no back-office capabilities): their own status (same data as `/api/me/status`).

The dashboard is intentionally lightweight — a health check, not an analytics surface. The **Missing submissions** section can take a few seconds to load on a full registry because it evaluates every required KPI across all services; see [setup/performance.md § Response times](../setup/performance.md#response-times). The **primary way to explore the data is PowerBI** (or any similar BI/OData client) pointed at the OData feed — see [setup/powerbi/](../setup/powerbi/README.md) — or `/api/admin/query` from a custom dashboard. The built-in [Explore](explore.md) page (quick in-app charts) and [reports](reports.md) are likewise basic conveniences, **not** a full analytics tool. Use PowerBI for real exploration, slicing and charting.

## Services console (accounts with no back-office capabilities)

An account with no back-office capabilities (a typical **Service**) gets a slimmer console (Dashboard + Submissions only). They can:

- View their own submissions (the **Submissions** page is automatically filtered to their account).
- Create submissions through the same on-behalf-of form (without picking the service — it's pinned to themselves).
- Edit submissions whose cadence window is still open. Editing a closed-window submission is blocked with a clear validation error.

This is meant for services that don't (yet) have an automated submitter and prefer to enter data through the UI for now. The forms are identical to the admin-side ones described in [submissions.md](submissions.md).

## Where to go next

- Ready-made [example schemas](../../examples/schemas/) and [report templates](../../examples/reports/html/) you can upload as-is or adapt — see [schemas.md § Example schemas](schemas.md#example-schemas-to-start-from) and [reports.md § Sample reports](reports.md#sample-reports).
- The deep-dive on each task lives in the focused pages linked at the top of this page.
- For the API surface a Service account hits programmatically, see [client/api.md](../client/api.md).
- For the auth model and how API keys are issued/verified, see [architecture/authentication.md](../architecture/authentication.md).
- For the OData feeds used by PowerBI and similar tools, see [setup/powerbi/](../setup/powerbi/README.md).
- For data-subject rights and retention (EU GDPR; UK GDPR / DPA 2018 apply the same), see [gdpr.md](../gdpr.md).

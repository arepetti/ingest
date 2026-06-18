# Integrations (Microsoft Teams)

**Integrations** prompt the people responsible for a service to fill in the KPI values that are still **outstanding** — without leaving Microsoft Teams. A bot sends an interactive **Adaptive Card** to a configured user or channel; the recipient types the values into the card and submits, and Ingest records the submission exactly like a manual entry in the console. Prompts go out on a **daily schedule** or **on demand**.

It's a **Settings → Integrations** section gated by the `integrations:read` capability (managing them needs `integrations:manage`) — in the Admin default bundle, but grantable to any non-admin — and it only appears when the feature is switched on server-side (`Integrations:Enabled`, **on by default** — see [setup/ms-teams.md → Configuration reference](../setup/ms-teams.md#configuration-reference)). When the switch is off the section is hidden and every `/api/admin/integrations/*` endpoint returns 404, mirroring the email and webhook master-switch patterns.

> **One-time setup first.** Before integrations can send anything, an administrator must register an Azure Bot, package and upload a small Teams app, and enter the bot credentials in Ingest. That deployment walk-through lives in [setup/ms-teams.md](../setup/ms-teams.md). This page covers day-to-day operation once that's done.

## What gets asked

An integration covers a **scope** of services and schemas. When it runs, Ingest works out which **required** values are currently outstanding within that scope (the same data behind the **Missing submissions** dashboard) and builds one card per service/schema/period that needs attention. The card:

- Shows only the **active** fields. Values hidden or disabled by a schema's `Visible if` / `Enabled if` rules are **omitted** — the bot evaluates those conditions as the recipient answers, so a field that doesn't apply is never asked.
- Surfaces **warnings** inline, the same soft-warning text an editor sees in the console, so the recipient can reconsider an unusual-but-legal value before submitting.
- Does **not** support notes.

Submitting the card writes the values through the normal submission path (recorded as a **manual** submission), so all the usual validation, cadence, approval, notification, and webhook behaviour applies.

## The Teams connection

The **Settings → Teams connection** subpage holds the bot's Microsoft Entra credentials. These are shared by every integration:

- **App (client) ID** — the bot's Microsoft App ID.
- **Tenant ID** — your directory (tenant) id; leave blank for a multi-tenant bot.
- **Single-tenant app registration** — turn on if you registered the bot as single-tenant.
- **Bot secret** — the client secret. It's **write-once**: stored encrypted at rest (the same pattern as SMTP settings under [settings.md → Email](settings.md)) and never shown again. Tick **Set / Change the bot secret** to enter a new value; leave the field blank while ticked to clear it.

Click **Test connection** to confirm Ingest can authenticate to Microsoft Entra / Bot Framework with the saved credentials. A green result means the credentials are valid; a red one carries the error to help you fix the App ID, secret, or tenant. The badge at the top of the card shows **Configured** / **Not configured** at a glance.

> Until the connection is configured, the **Integrations** list shows a warning and sends will not succeed — set the connection up first.

## Creating an integration

On **Settings → Integrations**, click **Add integration** and fill in:

- **Label** — a friendly name shown only in this list.
- **Enabled** — uncheck to keep the integration but stop it running (it's still skipped by the scheduler and by **Run now**).
- **Send to** — **A user** or **A channel**.
- **User id / Channel id** — the stable identifier of the target (Entra object id, UPN, or email for a user; the channel id for a channel). See [setup/ms-teams.md](../setup/ms-teams.md) for how to find these.
- **Display name** — optional friendly label for the target.
- **Services** — tick **All services**, or pick a subset. Leave as "All" to cover every service.
- **Schemas** — tick **All schemas**, or pick a subset.
- **Frequency** — how often the pass looks for outstanding values, plus an **hour** and **minute** in **UTC**:
  - **Daily** — every day.
  - **Weekly** — on the selected weekdays (leave empty for every day).
  - **Monthly** — on (or after) a chosen day of the month, or the **last day**.
  - **Quarterly** / **Semi-annually** / **Yearly** — repeating from a chosen **anchor month**, on (or after) a chosen day of the month (e.g. quarterly from February runs in Feb, May, Aug, Nov).

  The frequency only controls *when the check runs* — it doesn't have to match a KPI's cadence. Because the outbox de-duplicates per outstanding period, the Monthly-and-longer frequencies fire on a forgiving "on or after day N" basis within the eligible month, so a one-day server outage doesn't skip that period, and an integration covering many schemas with different cadences is still prompted once per period each.

Click **Create integration**. Selecting an existing row opens the same editor to change its details.

> **The bot must already be in the conversation.** Ingest can only send a proactive card to a user or channel that has *initiated contact* with the bot at least once (a 1:1 message, or adding the bot to a channel). If you've created an integration but cards never arrive, that's almost always the cause — see the troubleshooting table in [setup/ms-teams.md](../setup/ms-teams.md#troubleshooting).

## Running, testing and toggling

Each row's **⋮** actions menu offers:

- **Run now** — evaluate this integration immediately, regardless of its schedule, and enqueue prompts for everything currently outstanding in scope. The result reports how many prompts were sent and skipped.
- **Send test** — enqueue a single diagnostic card to the target, to confirm the full round trip (delivery → fill-in → submit → submission recorded) without waiting for outstanding work to exist.
- **Enable / Disable** — flip the enabled flag without opening the editor.
- **Delete** — remove the integration. Past deliveries are retained for audit.

The **⋯** menu above the list has **Refresh**.

## How sends are delivered

Like outgoing email and webhooks, prompts go through a durable **outbox** so a card is never lost if Teams is briefly unreachable:

- The **scheduler** (an in-process timer, cadence `Integrations:Scheduler:PollMinutes`) enqueues due integrations. You can disable it and drive scheduling from an external cron that calls the admin API instead.
- The **worker** (cadence `Integrations:Worker:PollSeconds`) drains the outbox, retrying failures with backoff up to `Integrations:Worker:MaxAttempts` before marking a delivery permanently failed.
- Each (event, integration) pair is **de-duplicated**, so re-running an integration for the same outstanding period won't spam the target with duplicate cards.

See [setup/ms-teams.md → Configuration reference](../setup/ms-teams.md#configuration-reference) for the full list of server-side switches.

## Where to go next

- [setup/ms-teams.md](../setup/ms-teams.md) — the one-time Azure + Teams setup, configuration reference, and troubleshooting.
- [../../examples/integrations/ms-teams/](../../examples/integrations/ms-teams/) — the Teams app manifest template and packaging notes.
- [settings.md](settings.md) — the rest of the Settings hub (email, notifications, retention).
- [webhooks.md](webhooks.md) — the other outbound channel; the same outbox/retry pattern.

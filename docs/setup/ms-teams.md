# Microsoft Teams integration

Ingest can prompt service owners to fill in **outstanding required KPI values** directly in Microsoft Teams. A bot sends an interactive **Adaptive Card** to a configured user or channel; when the recipient submits the card, Ingest records the submission like any other write. Integrations can run on a **daily schedule** or be triggered **on demand** from the admin console.

This page is the authoritative deployment walk-through — Azure Bot registration, Teams app packaging, and wiring the bot to your Ingest host. For day-to-day operation (creating integrations, schedules, test sends), see [../admin-user-guide/integrations.md](../admin-user-guide/integrations.md).

> **Prerequisite:** Ingest must already be deployed and reachable over **public HTTPS**. If you haven't stood up the service yet, start with [hosting.md](hosting.md) (or [quickstart.md](quickstart.md) for a local evaluation only — a local URL won't work for the Bot Framework messaging endpoint).

## What you need

Before you begin, confirm you have:

- An **Azure subscription** where you can create an Azure Bot resource and a Microsoft Entra app registration.
- A **Microsoft 365 organizational tenant** with **Microsoft Teams** — the kind councils and businesses use with work/school accounts. You need a Teams administrator (or equivalent) who can enable custom app upload.
- **Ingest reachable over public HTTPS** — the Bot Framework connector must POST to your messaging endpoint from the internet. A hostname behind Azure Container Apps ingress, App Service, or your own reverse proxy is fine; `localhost` is not.
- The **Integrations feature enabled** on the server (`Integrations:Enabled`, **on by default** — see [Configuration reference](#configuration-reference) below).

> **Consumer / personal Teams is not supported.** The free Teams app preinstalled on Windows (personal Microsoft accounts) cannot sideload custom apps or bots. You need an organizational tenant.

## Why a bot

The card Ingest sends is **interactive** — recipients type values into form fields and click **Submit**. Only a real **Teams bot** (registered through the **Azure Bot Service** / Bot Framework) can receive that submit action and return it to Ingest.

**Incoming webhooks**, **Power Automate** "post to channel" connectors, and similar one-way notification URLs can *display* a card, but they **cannot capture card input**. For KPI capture in Teams you must register a bot, set its messaging endpoint to Ingest, and sideload a small Teams app package that exposes the bot in your tenant.

## Step-by-step setup

### 1 — Create an Azure Bot resource

In the [Azure portal](https://portal.azure.com), create an **Azure Bot** resource. The **F0 (Free)** pricing tier covers the Microsoft Teams channel for typical council workloads.

When prompted for the Microsoft App ID, either:

- **Create a new Microsoft App ID** — Azure creates a matching **Microsoft Entra app registration** for you, or
- **Use an existing registration** if your org already has one.

Choose **Multi Tenant** or **Single Tenant** to match your org's policy (single-tenant is common for internal council bots).

### 2 — Note the App ID, tenant id, and create a client secret

From the Azure Bot resource (or the linked **App registration** in Microsoft Entra):

1. Copy the **Microsoft App ID** (also called the **client id**).
2. Copy the **Directory (tenant) id** — on single-tenant apps this is your org's tenant; on multi-tenant bots you still need the tenant id of the org that owns the bot registration.
3. Under **Certificates & secrets**, create a **client secret**, copy its **value immediately** (it is shown only once), and store it in your password manager.

You will enter these three values in Ingest later — they are **not** configuration-file settings.

### 3 — Set the messaging endpoint

In the Azure Bot resource, set **Messaging endpoint** to:

```text
https://<your-ingest-host>/api/integrations/teams/messages
```

Replace `<your-ingest-host>` with the public hostname of your deployment (the same FQDN you use for the admin SPA — see [hosting.md → Step 9](hosting.md#step-9--grab-the-fqdn)).

The endpoint must be **HTTPS**, use a publicly trusted certificate, and accept POST requests from the Bot Framework connector. If Ingest sits behind a reverse proxy, ensure forwarded headers are enabled (`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` — see [configuration.md](configuration.md#hosting--observability)).

### 4 — Enable the Microsoft Teams channel

In the Azure Bot resource, open **Channels**, add **Microsoft Teams**, and save. No extra configuration is required for a standard 1:1 or channel bot.

### 5 — Build the Teams app package

A Teams app manifest template lives in [examples/integrations/ms-teams/](../../examples/integrations/ms-teams/):

1. Open `manifest.json` and:
   - Set `"id"` to a **new unique GUID** (generate one — it must not collide with another app in your tenant).
   - Replace `{{BOT_APP_ID}}` with the **Microsoft App ID** from step 2.
   - Replace `{{INGEST_HOST}}` with your public hostname **without** a scheme (e.g. `ingest.example.org`).
2. Add icon files alongside the manifest:
   - `color.png` — **192×192** px
   - `outline.png` — **32×32** px, transparent background
3. Zip exactly these three files at the **root** of the archive (not inside a subfolder): `manifest.json`, `color.png`, `outline.png`.

The resulting `.zip` is the **Teams app package** you will upload in step 6.

### 6 — Allow and upload the custom app in Teams

In the [Teams admin center](https://admin.teams.microsoft.com):

1. Go to **Teams apps → Setup policies** (org-wide or the policy assigned to your pilot users) and ensure **Upload custom apps** is **On**. If your tenant blocks sideloading, the package cannot be installed until a Teams admin changes this.
2. Upload the app package — either through the admin center (**Manage apps → Upload new app**) or by sideloading in the Teams client for a pilot user, depending on your org's process.

After upload, the bot appears in your tenant's app catalog (or the pilot user's **Built for your org** apps).

### 7 — Connect the bot in Ingest

Sign in to the Ingest admin console as an administrator and open **Settings → Integrations → Connection**.

Enter:

| Field | Value |
|-------|-------|
| **App (client) ID** | Microsoft App ID (client id) from step 2 |
| **Tenant ID** | Directory (tenant) id from step 2 (leave blank for a multi-tenant bot) |
| **Single-tenant app registration** | On if you chose **Single Tenant** in step 1 |
| **Bot secret** | The client secret value from step 2 (tick "Set the bot secret" to enter it; write-once and encrypted at rest) |

The messaging endpoint itself is configured on the **Azure** side (step 3), not in Ingest. Click **Test connection**. A successful test confirms Ingest can authenticate to Bot Framework / Microsoft Entra with these credentials.

> Bot credentials are stored in the **database** (encrypted at rest), not in `appsettings.json` or environment variables — the same pattern as SMTP settings under **Settings → Email**.

### 8 — Install the bot and create an integration

The bot must be **added to a conversation** before Ingest can send proactive cards there:

- For a **single user**, open a **1:1 chat** with the bot and send any message (this establishes a conversation reference).
- For a **channel**, add the bot to the team/channel (mention it or use **Add to a team** from the app details page).

Then, in Ingest, go to **Settings → Integrations** and **Add integration**:

- **Target** — the user or channel you installed the bot into.
- **Service / schema scope** — limit which service account and schema the card covers; leave blank for **all** services and schemas.
- **Frequency** — Daily, Weekly (chosen weekdays), Monthly, Quarterly, Semi-annually, or Yearly (with a day-of-month and, for the longer periods, an anchor month), at a time in UTC — or rely on the row's **Run now** / **Send test** actions only.

Use the integration row's **Send test** action to push a card immediately and confirm the full round trip (delivery → fill-in → submit → submission recorded), or **Run now** to enqueue prompts for everything currently outstanding. See [../admin-user-guide/integrations.md](../admin-user-guide/integrations.md) for ongoing operation.

## Configuration reference

*What* to send, *where*, and *when* is admin data under **Settings → Integrations**; configuration only carries the master switch and background-worker cadence (mirroring the [Webhooks](configuration.md#webhooks) worker pattern).

| Key | Default | Notes |
|-----|---------|-------|
| `Integrations:Enabled` | `true` | Master switch. When `false`, the feature is inert — no scheduler or worker starts, the **Settings → Integrations** section is hidden, and integration endpoints return 404. |
| `Integrations:Scheduler:Enabled` | `true` | Whether an in-process scheduler enqueues due integration sends on a timer. Set `false` to drive scheduling from an external cron hitting the admin API instead. |
| `Integrations:Scheduler:PollMinutes` | `15` | How often the in-process scheduler checks for due integrations (minutes). |
| `Integrations:Worker:Enabled` | `true` | Whether an in-process background service drains the send outbox on a timer. Set `false` to drive sending from an external scheduler instead. |
| `Integrations:Worker:PollSeconds` | `15` | How often the in-process worker wakes up (seconds). |
| `Integrations:Worker:MaxAttempts` | `6` | Send attempts (with backoff) before a delivery is marked permanently failed. |
| `Integrations:Worker:BatchSize` | `25` | Max sends processed per worker pass. |

When passing keys through environment variables, use `__` for nesting — e.g. `Integrations__Enabled=false`. Full precedence rules are in [configuration.md](configuration.md#how-configuration-is-sourced).

## Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| Can't upload or sideload the app | **Upload custom apps** is disabled in Teams admin center setup policies, or your account isn't in a policy that allows it. Ask a Teams admin to enable sideloading for your pilot group. |
| **Test connection** fails | Wrong App ID, expired or mistyped client secret, wrong tenant id, or messaging endpoint URL mismatch. Regenerate the secret in Entra if in doubt. |
| Cards never arrive | Bot not **added to the target conversation** — Ingest needs a stored conversation reference from a prior 1:1 message or channel install. Open a chat with the bot or add it to the channel, then retry **Send test**. |
| Bot receives messages but submit doesn't record | Messaging endpoint not reachable (firewall, private network, wrong hostname). Confirm `https://<host>/api/integrations/teams/messages` is publicly reachable and returns a Bot Framework–compatible response. |
| Works in dev, fails in prod | Missing `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` behind a reverse proxy, or TLS termination misconfiguration. |
| "Works on my laptop Teams" but not for colleagues | **Personal / consumer Microsoft accounts** don't support custom bots. Everyone must use the **organizational** tenant where the app was uploaded. |
| Sends stop after ~90 days | **Client secret expired** in Entra — create a new secret and update **Settings → Integrations → Connection**. |

## See also

- [../admin-user-guide/integrations.md](../admin-user-guide/integrations.md) — day-to-day usage: targets, schedules, scopes, test sends, and audit.
- [../../examples/integrations/ms-teams/](../../examples/integrations/ms-teams/) — Teams app manifest template and packaging notes.
- [hosting.md](hosting.md) — deploy Ingest to Azure with a public HTTPS hostname.
- [configuration.md](configuration.md) — full configuration reference, including how env vars map to keys.

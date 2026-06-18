# Microsoft Teams app manifest (Ingest)

A **Teams app package template** for the Ingest KPI Assistant bot. The bot sends interactive Adaptive Cards to users or channels, prompting them for outstanding required KPI values; replies are posted back to your Ingest deployment at `https://<your-ingest-host>/api/integrations/teams/messages`. Bot credentials (App ID, secret, tenant) are configured in the Ingest admin console under **Settings > Integrations > Connection** — not in this folder.

> **Full setup walk-through:** see [docs/setup/ms-teams.md](../../../docs/setup/ms-teams.md) (Azure app registration, Bot Service, messaging endpoint, and admin-console connection).

## What to edit

Before packaging, replace the placeholders in `manifest.json`:

| Placeholder | Replace with |
|-------------|--------------|
| `id` | A new GUID for this app package (generate one; do not reuse the sample value) |
| `{{BOT_APP_ID}}` | The Microsoft Entra application (client) ID of your bot |
| `{{INGEST_HOST}}` | Your Ingest hostname only — e.g. `ingest.example.org` (no `https://`) |

You must also supply the two icon files listed below; they are not included in this repo.

## Required icons

| File | Size | Notes |
|------|------|-------|
| `color.png` | 192×192 | Full-color app icon |
| `outline.png` | 32×32 | Transparent outline icon |

Place both files in this folder alongside `manifest.json`.

## How to package

Zip **only** these three files at the root of the archive — no nested folder:

```
manifest.json
color.png
outline.png
```

Upload the `.zip` when sideloading the app in Teams or submitting it to your organisation's app catalogue.

## See also

- [Integrations index](../README.md)
- [docs/setup/ms-teams.md](../../../docs/setup/ms-teams.md) — full Azure and Teams setup

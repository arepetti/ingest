# The real cost of ownership (in effort)

This page is for the **people deciding whether to adopt Ingest** — the IT manager who runs the analyst team, and the service managers whose data flows in. It is deliberately *not* about money (for hosting cost see [setup/hosting.md](setup/hosting.md) and the [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/)). It's about **effort**: the work *you* take on to stand the thing up, keep it running, and recover it when something breaks.

Owning any KPI system is real work. The goal here is to make that work **explicit** so adopting Ingest is an informed choice, not a surprise three months in — and to be honest about what is *mandatory*, what is *optional*, what can run *where*, and how big each piece actually is.

> **Read this alongside the project's status.** Ingest is open-source, maintained by a single developer, offered **as-is with no SLA or warranty** ([../SUPPORT.md](../SUPPORT.md)). Most of what follows is the practical meaning of "plan to self-support". If you'd rather buy those guarantees than own them, that's a procurement decision — but you should still understand the effort below so you know what you're paying someone else to do.

## The honest comparison: what your current setup already costs

Before tallying Ingest's costs, be fair about the baseline. A pile of hand-filled **Excel sheets**, **SharePoint lists**, shared network drives, and the occasional Access database is *cheaper to start* — there's nothing to deploy. But it carries **the same ownership duties**; they're just hidden, undocumented, and usually resting on one person's laptop:

| Duty | Hand-made Excel / SharePoint | Ingest |
|------|------------------------------|--------|
| **Hosting** | The file share, the SP tenant, the one PC that runs the macro. Owned by IT whether or not anyone admits it. | One container + one database, explicitly documented. |
| **Backups** | "OneDrive version history", maybe. Often nobody knows. | Managed PITR backups (Cosmos) or your `mongodump` — documented. |
| **Recovery** | No runbook. When the file corrupts or the author leaves, you reconstruct from memory. | A starting-point DR runbook you adapt ([setup/disaster-recovery.md](setup/disaster-recovery.md)). |
| **Access control** | Sharing links and good intentions; no audit of who changed what. | Capability-based accounts, API keys, full audit log, soft-delete. |
| **Data quality** | Whatever the typist entered. Errors found in the leadership meeting. | Server-side validation + cadence enforcement *before* data lands. |
| **Integrations** | A human extracts a report and **re-types it** every period. | A script you write once (see [§ API integrations](#api-integrations-arent-free)). |
| **Knowledge** | Tribal. The spreadsheet's logic lives in one head. | Written docs, in-repo, version-controlled. |

So the question isn't "free vs. costly" — it's **"undocumented effort on one person vs. explicit effort you can plan, share, and hand over."** Keep that framing in mind for every cost below.

## Tiers of ownership: pick the smallest thing that solves your problem

You don't start at the top. There's a ladder, and most teams climb it deliberately:

| Tier | What it is | Ownership effort | Good for |
|------|------------|------------------|----------|
| **Evaluate** | Local Docker [quickstart](setup/quickstart.md) — one `docker compose up` | Near-zero, throwaway | Proving the cadence/validation value on your own machine |
| **Pilot / light use** | Azure [free-tier path](setup/hosting.md#free-tier---a-0-evaluation-deployment) (Container Apps free grant + Cosmos vCore Free Tier + GHCR) | Low — a few CLI commands | A persistent demo or small internal pilot |
| **Production** | Container Apps + Cosmos vCore **M30** ([setup/hosting.md](setup/hosting.md)) | The real ownership tier — everything below | Feeding live leadership KPIs |

The pilot tier comes with honest caveats baked into the docs: **no HA, no SLA, no backup/restore, 32 GB cap, and the cluster auto-pauses after 60 days idle** ([free-tier caveats](setup/hosting.md#free-tier-caveats)). That's fine for a pilot and dishonest as a production base — don't let a successful pilot quietly become production without climbing the last rung.

## Mandatory ownership (you can't skip these)

These exist in every real deployment. They're mostly **one-off** with a small **ongoing** tail.

### Hosting — choose one

The app is a single image (API + admin SPA) plus one MongoDB. Where it runs is your call ([setup/hosting.md](setup/hosting.md)):

| Option | One-off effort | Ongoing effort | When to pick it |
|--------|----------------|----------------|-----------------|
| **Container Apps + Cosmos vCore** (recommended) | Moderate — the [step-by-step guide](setup/hosting.md) is ~14 commands | Low — managed DB, revision-based rollout | Default for most councils |
| **App Service (Linux container)** | Moderate — similar plumbing, deployment slots | Low | You already standardise on App Service |
| **AKS** | Higher — you bring the manifests and secrets flow | Higher — you own the cluster | Your org already runs Kubernetes |
| **Self-hosted MongoDB** | Higher — you run the database | **Highest** — patching, backups, HA all yours | You must keep data on specific infrastructure |

The recommendation exists for a reason: managed Cosmos vCore moves the heaviest ongoing duty (running a database) off your plate. Self-hosting Mongo is the most control and the most work.

### Secrets and first sign-in

Every deployment must set and safeguard a few secrets ([setup/hosting.md Step 5](setup/hosting.md#step-5--store-secrets-in-key-vault-recommended), [Step 8](setup/hosting.md#step-8--create-the-container-app), [Step 10](setup/hosting.md#step-10--sign-in-with-the-bootstrap-admin-key)):

- **`ApiKey:Pepper`** — a long random value. **Back it up offline.** It's the one truly irreplaceable secret: lose it and every stored API-key hash becomes unverifiable ([setup/disaster-recovery.md](setup/disaster-recovery.md)).
- **The Mongo connection string** — stored in Key Vault (recommended) or app secrets.
- **Bootstrap admin key** — used for the first sign-in, then **rotated and revoked**.

Effort: small one-off, plus a habit of rotating keys. Compared to the baseline, this is *more* discipline than a shared spreadsheet password — but that's the point.

### Edge concerns the app deliberately leaves to you

Ingest authenticates every request but **does not** do TLS termination, rate limiting, or IP allow-listing in-app — these belong at the ingress/proxy ([setup/hosting.md § Network controls](setup/hosting.md#network-controls)). On Container Apps, HTTPS is on by default; rate limits and IP rules are a WAF/Front Door/APIM/Nginx config. Effort: low-to-moderate one-off, depending on how locked-down your org needs it.

### Upgrades and patching

You pull new image versions when you choose to. Container Apps does revision-based zero-downtime rollout; the bundled CI outline ([Step 14](setup/hosting.md#step-14--cicd-outline)) automates it. Effort: low but **ongoing** — security patching is never "done".

## Optional features — pay-as-you-need effort

This is where "not everything is needed everywhere" matters most. Each feature is independently switchable; turn on only what your workflow needs.

| Feature | Default | Setup effort | Where it runs | Skip it if… |
|---------|---------|--------------|---------------|-------------|
| **Email / SMTP notifications** | On (inert until configured) | **Low** — point it at an SMTP server in **Settings → Email**; password encrypted at rest | In-process worker (or external scheduler) | You don't need upcoming/missed/warning emails |
| **Approval workflow** | Off | **Minimal** — a config flag + per-schema/rule policy | In-app | You trust submissions to go live immediately |
| **Webhooks** | Off | **Low-moderate** — register an endpoint + signing secret; verify HMAC on your side | In-process outbox worker | Nothing external needs to react to events |
| **SSO (Microsoft / Google)** | Off | **Moderate** — IdP app registration, client secret, redirect URI, link each user to an account | App + your IdP | API-key login is enough for your admins |
| **Microsoft Teams integration** | On (inert until connected) | **High — a real project** | Azure Bot + Entra app + your public HTTPS host | You won't capture KPIs from Teams cards |

Notes that change the effort estimate:

- **Email** is the cheapest win and the one most teams turn on first ([setup/hosting.md § Optional email](setup/hosting.md#optional--email--notifications)). No Azure infrastructure — it's runtime config from the SPA.
- **SSO** is off by default and the app runs identically without it ([setup/hosting.md § SSO](setup/hosting.md#optional--enable-single-sign-on-microsoft--google)). The work is mostly on the IdP side and the per-user account linking.
- **Webhooks** ([admin-user-guide/webhooks.md](admin-user-guide/webhooks.md)) are durable and retrying, so the ongoing burden is low once a receiver is built — but *building and verifying the receiver* is your code.
- **Teams** ([setup/ms-teams.md](setup/ms-teams.md)) is the heaviest by far: an Azure Bot resource, an Entra app registration whose **client secret expires (~90 days)** and must be rotated, Teams-admin sideloading of an app package, and a publicly-reachable HTTPS endpoint. Budget it like a small project, not a checkbox. Its payoff is real (managers fill in KPIs from a Teams card) — just don't underestimate the standup.

## Disaster recovery — the tools are provided, the *plan* is yours

This is the most important "owned" responsibility, and the easiest to wave away until it's too late.

**What Ingest / managed hosting give you:**

- Automatic **point-in-time backups** with 35-day retention and optional **in-region HA** on the recommended Cosmos setup ([setup/hosting.md § Backups](setup/hosting.md#backups)) — nothing to configure.
- An in-app **export/import** tool for small datasets and environment seeding (explicitly **not** your production backup mechanism).
- A **starting-point DR runbook** with failure scenarios and step-by-step restore procedures ([setup/disaster-recovery.md](setup/disaster-recovery.md)).

**What you must own** (the doc says this in bold itself — it's a *template*, not a finished plan):

- Your **RPO/RTO** targets, agreed with business owners.
- **Regulatory retention** beyond the 35-day window (means your own `mongodump` archive).
- The **region-outage strategy** — single-region is the default; cross-region replication is a *decision to make now, not during an incident*.
- An **offline backup of `api-key-pepper`** your DR process can reach without Azure access.
- Actually **testing a restore** — "an untested DR plan is a guess."

Effort: moderate one-off to write and sign off your version, plus a recurring **drill** (e.g. quarterly). Honest comparison: the Excel/SharePoint baseline has **no runbook at all** — recovery there is "hope the file's in version history and the author still works here." Ingest hands you a template; the work is adapting and rehearsing it.

## API integrations aren't free

The biggest lever Ingest offers is **stop re-typing numbers** — letting a script push KPIs straight from the source system. That's a genuine cost transfer, not a free lunch: someone has to **write, test, host, schedule, and secret-manage** one integration per source system.

**Why it's bounded, not scary:**

- The repo ships **copy-pasteable examples** in Python, PowerShell, C#, and Java, for both a **CSV/Excel export** source and a **vendor REST API** source, across a waste-collection and an HR schema (including **MHR iTrent**) — see [../examples/integrations/README.md](../examples/integrations/README.md). "The field mappings are the part worth copying."
- Each example has a **dry-run switch** that prints the exact payload without calling the API — so you test mappings safely first.
- The contract is tiny: one header (`X-Api-Key`) and one endpoint (`POST /api/submissions`) — see [client/api.md](client/api.md).
- A job can ask **`GET /api/me/status`** "what's still outstanding?" and assert a period is closed out — so integrations become self-checking instead of fire-and-forget.

**Where it can run** (effort scales with robustness):

| Where | Effort | Reality |
|-------|--------|---------|
| **Windows Task Scheduler on an operator's PC** | Lowest | The docs call this "deliberately naive" — only runs when that machine is on and signed in, key sits in a local file. Fine for a trial. |
| **Azure Function (timer) / Logic App / Power Automate / cron on a server** | Moderate | The production pattern: always-on, key in a secrets store. Same mapping logic; only *where it runs and how the secret is stored* change. |
| **Inside an existing system / integration platform** | Varies | KPIs flow out as a side effect of work already happening. |

Be honest about the trade you're making: the manual "**extract a report → type it into Excel**" loop *is* the integration cost you pay **today** — every period, in human hours and transcription errors. Ingest converts that recurring human cost into a **one-off engineering cost** (write + test the script) plus a small ongoing one (keep it running). For a KPI collected every week, that maths usually favours the script quickly.

## What runs where — the summary matrix

| Piece | Mandatory? | Where it can run | Effort (one-off / ongoing) |
|-------|------------|------------------|----------------------------|
| App container + database | **Yes** | Container Apps+Cosmos / App Service / AKS / self-hosted | Moderate / low (managed) to high (self-hosted) |
| Secrets + bootstrap admin | **Yes** | Key Vault or app secrets | Small / minimal (rotation) |
| TLS / rate-limit / IP rules | **Yes** (TLS); rest as needed | Ingress / WAF / proxy | Low-moderate / minimal |
| Image upgrades | **Yes** | Your CI | Low / ongoing |
| Email notifications | Optional | In-process worker + your SMTP | Low / minimal |
| Approval workflow | Optional | In-app | Minimal / none |
| Webhooks | Optional | In-process worker + your receiver | Low-moderate / low |
| SSO | Optional | App + your IdP | Moderate / low (secret rotation) |
| Teams integration | Optional | Azure Bot + Entra + public HTTPS | **High** / moderate (secret expiry) |
| DR plan + drills | **Yes** (own it) | Your process | Moderate / recurring |
| Source-system integrations | Optional (but the whole point) | PC / Azure Function / Logic App / cron | Per-source one-off / low ongoing |

## A quick decision guide

Scope the **minimum viable footprint** by answering these:

- **Just evaluating?** Quickstart locally. Stop. No ownership.
- **Small pilot, can tolerate downtime and no backups?** Free-tier path. Revisit before it becomes production.
- **Production?** Container Apps + Cosmos M30, secrets in Key Vault, TLS + a sane rate limit, image CI, **and a DR plan you've actually tested**. Everything else is optional.
- **Need email reminders?** Turn on SMTP — cheapest high-value add.
- **Admins want org logins?** Add SSO. Otherwise skip it.
- **Want services to react to events, or chase stragglers automatically?** Webhooks and/or the upcoming/missed notifications.
- **Want KPI capture inside Teams?** Budget the Teams integration as a project, not a toggle.
- **Want to stop re-typing numbers?** Pick the closest [integration example](../examples/integrations/README.md), adapt the mappings, run it on a schedule somewhere always-on. Do this per source system.

You do **not** need SSO, Teams, webhooks, or approvals to get value — a production host, validated submissions, the OData feed into Power BI, and one or two integrations already replace the worst of the spreadsheet pain.

## Related reading

- [setup/hosting.md](setup/hosting.md) — every deployment option, secrets, network controls, operational checklist.
- [setup/configuration.md](setup/configuration.md) — every setting the app reads.
- [setup/disaster-recovery.md](setup/disaster-recovery.md) — the DR template you must adapt and own.
- [setup/ms-teams.md](setup/ms-teams.md) — the Teams integration standup in full.
- [admin-user-guide/webhooks.md](admin-user-guide/webhooks.md) — outbound event delivery.
- [../examples/integrations/README.md](../examples/integrations/README.md) — the integration examples that cut the per-source effort.
- [../SUPPORT.md](../SUPPORT.md) and [../GOVERNANCE.md](../GOVERNANCE.md) — what "no SLA, plan to self-support" means, and how to share the load.

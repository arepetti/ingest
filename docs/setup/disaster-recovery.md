# Disaster recovery plan (starting-point draft)

> **⚠️ This is a starting point, not a finished plan.** It is a *template* written against the assumptions in [hosting.md](hosting.md) (Azure Container Apps + Cosmos DB for MongoDB vCore, single region). **Your organisation must review, adapt, and own it** before relying on it. In particular you are responsible for aligning it with:
>
> - **Local laws and regulations** (data residency, retention, breach notification, records management, sector rules).
> - **Your own internal policies and procedures** (change management, incident response, approvals, on-call rota).
> - **The exact details of your hosting setup** — region(s), tiers, whether HA and cross-region replication are actually enabled, backup retention, secret storage, networking, and who has access.
>
> The recovery objectives, retention windows, contacts, and step counts below are **placeholders and illustrative defaults**. Replace every *(fill in)* marker, validate every command against your live environment, and **test a real restore** before an incident forces you to. An untested DR plan is a guess.

This document explains how to recover Ingest after data loss or an outage. Ingest is deliberately simple: a **stateless** container (API + admin SPA in one image) plus **one MongoDB database**. That shapes the whole plan — almost all durable state lives in MongoDB, so "recovery" is mostly "get a good database back and point a fresh container at it".

## Assumptions this draft is written against

This plan assumes the **recommended production deployment** from [hosting.md](hosting.md). If your setup differs, revise the relevant sections.

| Component | Assumed configuration | Where it's set up |
|-----------|-----------------------|-------------------|
| Application | Azure Container Apps, single region, `--min-replicas 1`, stateless single image | [hosting.md Step 8](hosting.md#step-8--create-the-container-app) |
| Database | Cosmos DB for MongoDB vCore, **M30**, single region | [hosting.md Step 4](hosting.md#step-4--provision-cosmos-db-for-mongodb-vcore) |
| In-region HA | **Enabled** (`--shard-node-ha true`) | [hosting.md § High availability](hosting.md#high-availability) |
| Automatic backups | On by default, **35-day** point-in-time retention | [hosting.md § Automatic backups](hosting.md#automatic-backups-on-cosmos-db-for-mongodb-vcore) |
| Cross-region replication | **Not enabled** (single-region deployment) | optional add-on, see [scenario 4](#scenario-4--full-region-outage) |
| Secrets | `mongo-cs`, `api-key-pepper`, `bootstrap-admin-key` in **Azure Key Vault** | [hosting.md Step 5](hosting.md#step-5--store-secrets-in-key-vault-recommended) |
| Container image | In ACR (or GHCR), rebuildable from source via CI | [hosting.md Step 3](hosting.md#step-3--build-and-push-the-image) / [Step 14](hosting.md#step-14--cicd-outline) |
| Image source of truth | This git repository (`Dockerfile` at root) | repo |

> **Critical implication of single region.** With no cross-region replica, the loss of the *entire* Azure region is the worst case and has the longest recovery time. If your recovery objectives can't tolerate that, enabling cross-region replication is a design decision to make *now*, not during an incident — see [scenario 4](#scenario-4--full-region-outage).

## Recovery objectives (fill in)

Define these with your business owners; the values below are **placeholders** to be replaced.

| Objective | Definition | Target *(fill in)* |
|-----------|------------|--------------------|
| **RPO** (Recovery Point Objective) | Maximum acceptable data loss, measured in time | *(e.g. ≤ 5 minutes — bounded by Cosmos PITR granularity)* |
| **RTO** (Recovery Time Objective) | Maximum acceptable time to restore service | *(e.g. ≤ 4 hours for cluster restore; longer for full-region rebuild)* |
| **Retention** | How far back you must be able to restore | *(e.g. 35 days via PITR; longer needs your own `mongodump` archive)* |

> The 35-day PITR window caps how far back you can recover *by default*. If a regulation requires you to be able to restore data older than that, you must take and store your own periodic exports — see [§ Long-term archival](#long-term-archival-beyond-the-pitr-window).

## Roles and contacts (fill in)

| Role | Who | Contact |
|------|-----|---------|
| Incident lead | *(fill in)* | *(fill in)* |
| Azure / infrastructure owner | *(fill in)* | *(fill in)* |
| Application / Ingest admin | *(fill in)* | *(fill in)* |
| Data protection / compliance | *(fill in)* | *(fill in)* |
| Microsoft Azure support plan | *(fill in — plan tier & how to open a sev-A case)* | *(fill in)* |

## What to protect

Everything durable is in MongoDB; the rest is rebuildable from source or re-issued.

| Asset | Lives in | If lost… |
|-------|----------|----------|
| All registry data (submissions, samples, schemas, accounts, audit, settings, email/webhook config) | **MongoDB (Cosmos vCore)** | Restore from PITR backup or `mongodump` archive |
| `api-key-pepper` | **Key Vault** | **Cannot be regenerated** — losing it invalidates every stored API key hash (see [authentication.md](../architecture/authentication.md#configuration-knobs)). Back it up. |
| `mongo-cs` (connection string) | **Key Vault** | Rebuildable from the cluster, but back it up so you don't need portal access mid-incident |
| Container image | **ACR / GHCR** | Rebuild from git via CI ([Step 14](hosting.md#step-14--cicd-outline)) |
| Infrastructure definition | This repo / IaC *(fill in if you use Bicep/Terraform)* | Re-provision from the [hosting.md](hosting.md) steps or your IaC |
| SSO client secrets (if used) | Key Vault + IdP | Re-issue from the IdP and re-store |

> **The pepper is the one truly irreplaceable secret.** A restored database is useless if you can't authenticate against it because the pepper changed. Keep an offline copy of `api-key-pepper` somewhere your DR process can reach without Azure access (e.g. an org password manager / sealed envelope). Document this in your own procedures.

## Backup posture (summary)

See [hosting.md § Automatic backups](hosting.md#automatic-backups-on-cosmos-db-for-mongodb-vcore) for the detail. In short, on the assumed setup:

- **Automatic, continuous PITR backups** of the Cosmos cluster, 35-day retention, no setup required.
- Backup snapshots stored across **three availability zones** where supported.
- A restore **creates a new cluster** — it is not an in-place rollback. HA and networking are **not** carried over and must be re-applied.
- The **in-app Tools → Backup & restore** tools (admin SPA) are **not** part of this plan for production — they are in-memory, non-transactional exports meant for small datasets and copying between environments. Do not depend on them for DR. There are two: a **data backup** (the whole registry) and a separate **configuration backup** (the Settings-page configuration: approvals, email/notifications, webhooks, integrations and the Teams connection — see [admin-user-guide/tools.md](../admin-user-guide/tools.md)). The configuration backup can be a convenient way to re-seed settings onto a freshly restored cluster, but its encrypted secrets only decrypt on a deployment using the **same** `api-key-pepper` — the same constraint that makes the pepper the one irreplaceable secret above.

## Scenarios and runbooks

Each scenario below is a **skeleton runbook**. Walk through it in a drill, fill in the gaps for your environment, and keep the validated version under change control.

### Scenario 1 — Accidental data deletion or bad bulk write

*Someone deletes the wrong records, a faulty import corrupts data, etc.*

1. **Stop the bleeding.** If an automated job is still writing bad data, disable the offending service account / API key, or scale the app to zero (`az containerapp update --name $App --resource-group $Rg --min-replicas 0`) to freeze writes.
2. **Pick a restore point** *before* the damage. PITR lets you choose any time in the retention window.
3. **Restore to a new cluster** — follow the [Cosmos restore runbook](#runbook-restore-the-cosmos-cluster-from-pitr).
4. **Reconcile.** Because a restore creates a *new* cluster at a past point in time, any *legitimate* writes after that point are not on the restored copy. Decide with the data owner whether to (a) cut over to the restored cluster and accept the gap, or (b) export just the affected collections from the backup and selectively re-import into the live cluster. *(Define your org's preferred approach here.)*
5. Repoint the app and [validate](#post-recovery-validation).

### Scenario 2 — Cosmos node or availability-zone failure

*A database node fails, or one AZ goes down.*

- **If HA is enabled (assumed):** this is largely **automatic**. Cosmos fails over to the standby node; the connection string is unchanged and the app reconnects on its own. Action is usually limited to **confirming** the failover happened (Azure portal cluster status / metrics) and watching for elevated latency or errors during the transition.
- **If HA is *not* enabled:** the cluster is unavailable until Azure repairs the node. There is no fast remedy mid-incident — which is exactly why HA is on the [operational checklist](hosting.md#operational-checklist). Escalate to Azure support and, if downtime exceeds your RTO, consider restoring to a new cluster from PITR.

### Scenario 3 — Cosmos cluster lost or unrecoverable (still in-region)

*The cluster is deleted, corrupted, or otherwise gone, but the region is healthy.*

1. **Restore from PITR** to a new cluster — [runbook below](#runbook-restore-the-cosmos-cluster-from-pitr). (A *deleted* cluster's backups are retained for **7 days** — act promptly.)
2. Re-enable HA and networking on the new cluster.
3. Repoint the app and [validate](#post-recovery-validation).

### Scenario 4 — Full region outage

*The entire Azure region hosting the app and database is unavailable.*

This is the worst case for the **single-region** assumed setup. Your options, fastest first:

- **If cross-region replication is enabled** *(not in the default setup)*: fail writes over to the replica cluster in the secondary region using the global read-write connection string / role reversal, then deploy (or scale up) the container app in that region and repoint it. This is the only option that meets a tight RTO for a region loss — decide whether you need it **before** an incident.
- **If it is not enabled (assumed):** you must wait for the region to recover, **or** rebuild in another region from a backup. Backup snapshots are stored across availability zones *within* a region, so a true whole-region loss may mean falling back to your own off-region `mongodump` archive if you keep one. *(Document which path your org will take and the expected RTO — it is materially longer than the other scenarios.)*

> This scenario is where the gap between the default single-region design and your real recovery objectives is widest. **Resolve it as a deliberate architecture decision**, not in the heat of an outage.

### Scenario 5 — Lost or compromised secrets

| Lost secret | Recovery |
|-------------|----------|
| `mongo-cs` | Re-derive from the cluster (`az cosmosdb mongocluster list-connection-string`) and re-store in Key Vault. |
| `api-key-pepper` | **If you have a backup copy, restore it** — the database remains usable. **If it is truly lost**, every stored key hash is unverifiable: set a new pepper, then **every account must be re-issued an API key** (and SSO users re-link). Treat this as a major incident. |
| `bootstrap-admin-key` | Re-set a new value in Key Vault and bounce the app, or recover via a second bootstrap admin account — see [authentication.md](../architecture/authentication.md). |
| SSO client secret (if used) | Re-issue at the IdP, update the Key Vault secret, restart. |
| Suspected compromise of any secret | Rotate it, audit access, and follow your org's security incident procedure. *(fill in)* |

### Scenario 6 — Locked out of admin access

If all admin API keys are lost but the database and pepper are intact, use the **bootstrap admin** path: set `ApiKey:BootstrapAdminName` to a *new* name and `ApiKey:BootstrapAdminKey` to a known value, restart, sign in, then re-establish normal admin accounts. See [hosting.md Step 10](hosting.md#step-10--sign-in-with-the-bootstrap-admin-key) and [authentication.md](../architecture/authentication.md).

## Runbook: restore the Cosmos cluster from PITR

> Validate every command against your environment and Azure CLI version before relying on it. A restore creates a **new** cluster; the original is not modified.

1. **Choose the restore point** (UTC) — the latest point before the incident, within the retention window.
2. **Trigger the restore.** In the portal: cluster → **Settings → Point In Time Restore**, pick the time, name the new cluster. (CLI equivalents exist; confirm the exact `az cosmosdb mongocluster` restore syntax for your CLI version — see [Microsoft's restore guide](https://learn.microsoft.com/azure/cosmos-db/mongodb/vcore/how-to-restore-cluster).)
3. **Wait for provisioning** of the new cluster (same region / subscription / resource group as the source).
4. **Re-apply settings that do NOT carry over:**
   - **High availability** — re-enable if needed ([hosting.md § High availability](hosting.md#high-availability)).
   - **Networking / firewall** — re-add the `azure-services` firewall rule or VNet wiring so Container Apps can connect.
   - Alerts/metrics, if you configured any on the original.
5. **Get the new connection string** and append the database name (`/ingest`), exactly as in [hosting.md Step 4](hosting.md#step-4--provision-cosmos-db-for-mongodb-vcore).
6. **Repoint the app:** update the `mongo-cs` Key Vault secret (or the Container App secret) to the new value and restart the app so it reconnects:
   ```powershell
   az keyvault secret set --vault-name $Vault --name "mongo-cs" --value $NewMongoCs
   az containerapp revision restart --name $App --resource-group $Rg --revision <current-revision>
   ```
7. **Validate** — see below. The app re-creates indexes on startup (`MongoSetup.EnsureIndexesAsync`), so no manual index step is needed.
8. **Decommission the old cluster** once the new one is confirmed healthy and clients are cut over (mind your retention/forensic needs first).

## Runbook: redeploy the container app

The app is stateless and rebuildable, so this is fast as long as the image and secrets exist.

1. Ensure the image is available (in ACR/GHCR, or rebuild from git: `az acr build` / the bundled GitHub Action — [Step 14](hosting.md#step-14--cicd-outline)).
2. Re-run [hosting.md Step 8](hosting.md#step-8--create-the-container-app) (or your IaC) pointing at the correct image and the `mongo-cs` / `api-key-pepper` / `bootstrap-admin-key` secrets.
3. Re-attach the custom domain and certificate ([Step 11](hosting.md#step-11--configure-a-custom-domain-optional)) and health probes ([Step 12](hosting.md#step-12--health-probes-optional-recommended)).
4. [Validate](#post-recovery-validation).

## Post-recovery validation

Before declaring the incident resolved, confirm:

- [ ] `GET /health` returns `Healthy` and `GET /alive` succeeds.
- [ ] An admin API key authenticates (`GET /api/me` with `X-Api-Key`) — proves the pepper matches the restored data.
- [ ] Recent submissions and schemas are present and correct up to the expected recovery point.
- [ ] A test submission validates and persists.
- [ ] The OData feed (`GET /odata/samples`) returns data, and a Power BI refresh succeeds.
- [ ] Email/webhook/notification settings survived (or were reconfigured) if those features are in use.
- [ ] The audit log is intact.
- [ ] Clients/integrations have been repointed if the hostname changed.
- [ ] Old/temporary resources are cleaned up; secrets rotated if compromise is suspected.

## Long-term archival (beyond the PITR window)

If a regulation requires recovering data older than the **35-day** PITR window, the automatic backups are **not enough**. Schedule periodic `mongodump` exports and store them in your own durable, access-controlled, ideally immutable storage (with its own retention and disposal policy that matches your regulations). Test restoring from one of these archives — an archive you've never restored is not a backup. *(Define cadence, storage location, encryption, and retention here.)*

## Testing this plan

A DR plan is only real once it's been exercised. Suggested (adapt to your org):

- [ ] **Restore drill** — perform a PITR restore to a throwaway cluster and run the [validation checklist](#post-recovery-validation). Do this at least *(fill in — e.g. quarterly)* and after any major change.
- [ ] **Secret-recovery drill** — confirm you can retrieve `api-key-pepper` and `mongo-cs` from your offline backup without Azure portal access.
- [ ] **Redeploy drill** — rebuild the image from git and stand the app up against a test database.
- [ ] **Review** RPO/RTO, contacts, and assumptions whenever the architecture, region, tiers, or regulations change.
- [ ] Record the date, result, and lessons of each drill.

## Customisation checklist (do this before relying on the plan)

- [ ] Replace **every** *(fill in)* marker (objectives, contacts, cadences, archival policy).
- [ ] Confirm the [assumptions table](#assumptions-this-draft-is-written-against) matches your *actual* deployment (region, tier, HA on/off, replication on/off, retention).
- [ ] Decide and document the **region-outage** strategy ([scenario 4](#scenario-4--full-region-outage)) against your RTO.
- [ ] Verify the `api-key-pepper` backup exists and is recoverable offline.
- [ ] Align retention/archival with your **local regulations** and records-management policy.
- [ ] Validate every CLI command against your Azure CLI version and a test environment.
- [ ] Have the plan reviewed and signed off by your security/compliance and operations owners.
- [ ] Run the first restore drill and record the result.

## Related reading

- [hosting.md](hosting.md) — the deployment this plan assumes, including [HA](hosting.md#high-availability), [automatic backups](hosting.md#automatic-backups-on-cosmos-db-for-mongodb-vcore), and [network controls](hosting.md#network-controls).
- [configuration.md](configuration.md) — every setting referenced above (`ApiKey:Pepper`, `ConnectionStrings:ingest`, bootstrap admin, retention).
- [../architecture/authentication.md](../architecture/authentication.md) — why the pepper is irreplaceable and how key/SSO recovery works.
- [../admin-user-guide/settings.md](../admin-user-guide/settings.md) — the in-app Backup & restore tool and why it is *not* your production DR mechanism.
- [Microsoft Learn — Restore a Cosmos DB for MongoDB vCore cluster](https://learn.microsoft.com/azure/cosmos-db/mongodb/vcore/how-to-restore-cluster) — authoritative, current restore steps and retention numbers.

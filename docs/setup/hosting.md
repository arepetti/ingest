# Hosting

This guide explains how to host Ingest in **Microsoft Azure** end-to-end. The recommended target is **Azure Container Apps** backed by **Cosmos DB for MongoDB (vCore)** — a production-ready option that avoids running your own MongoDB cluster. Alternatives at the bottom for self-hosted MongoDB, App Service, or AKS.

The Ingest container is single-image: it bundles the compiled API **and** the built admin SPA. You don't need a separate static-web-app deployment.

> **Just want to try it?** Don't deploy to Azure — run it locally with Docker in a couple of minutes (no Azure account, no .NET SDK). See [quickstart.md](quickstart.md). This page is for a real, persistent deployment.

> **A note on cost.** The main guide below is not free: Cosmos DB for MongoDB vCore is billed per node-hour and the `M30` tier used below is a paid, always-on resource; Container Apps, ACR, and Log Analytics add smaller charges on top. Check the [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/) for current numbers in your region, and see [§ Tearing it down](#tearing-it-down) to stop the meter when you're done evaluating.

> **Want it for free?** You can deploy to Azure at roughly **$0** for evaluation or light use — Container Apps free grant + Cosmos DB vCore Free Tier + GHCR, no ACR. See [§ Free tier - a $0 evaluation deployment](#free-tier---a-0-evaluation-deployment). (Just trying it on your own machine instead? Use the [quickstart](quickstart.md).)

## TL;DR

```text
┌──────────────────────────────────────────────────────────────┐
│ Resource Group "rg-ingest-prod"                              │
│                                                              │
│   ┌──────────────────────┐    ┌─────────────────────────┐    │
│   │ Azure Container      │    │ Cosmos DB for MongoDB   │    │
│   │ Registry (ACR)       │    │ (vCore)                 │    │
│   │  - image: ingest:1.x │    │  - cluster: ingest-db   │    │
│   └──────────┬───────────┘    │  - db:      ingest      │    │
│              │                 └────────────┬────────────┘   │
│              │ pull (managed identity)      │                │
│              ▼                              │ MongoDB wire   │
│   ┌──────────────────────────────────────┐  │ protocol       │
│   │ Container Apps Environment           │  │                │
│   │  ┌─────────────────────────────────┐ │  │                │
│   │  │ App: ingest (HTTPS ingress)     │ │◄─┘                │
│   │  │  - secret: api-key-pepper       │ │                   │
│   │  │  - secret: mongo-cs             │ │                   │
│   │  │  - liveness: /alive             │ │                   │
│   │  │  - readiness: /health           │ │                   │
│   │  └─────────────────────────────────┘ │                   │
│   └──────────────────────────────────────┘                   │
│                                                              │
│   ┌──────────────────────┐                                   │
│   │ Key Vault            │ ← actual secret store, surfaced   │
│   │  - api-key-pepper    │   to Container App via secretref  │
│   │  - mongo-cs          │   (optional, recommended)         │
│   └──────────────────────┘                                   │
└──────────────────────────────────────────────────────────────┘
```

## Before you begin

You need:

- An Azure subscription where you can create resource groups, ACR, Container Apps, and Cosmos DB.
- The [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) installed and logged in (`az login`). Use a recent version (`az version` ≥ 2.60) and install the extensions the newer commands need — the CLI will usually offer to install them automatically, or do it up front:

  ```powershell
  az upgrade
  az extension add --name containerapp --upgrade
  ```

  (The `az cosmosdb mongocluster` commands ship with the core CLI but are relatively new, which is another reason to keep `az` current.)
- Docker — only if you build locally (Step 3). You can skip it and let `az acr build` or the bundled GitHub Action build the image for you.
- A clone of this repository, including the `Dockerfile` at the root.

> **Region availability.** Cosmos DB for MongoDB vCore and Container Apps aren't in every Azure region. Pick a region that offers both (the examples use `westeurope`). If a `create` call fails with a "not available in this location" error, try a larger region.

Pick names that follow your org's convention. The examples use:

| Resource        | Example name             |
|-----------------|--------------------------|
| Resource group  | `rg-ingest-prod`         |
| Location        | `westeurope`             |
| ACR             | `acringestprod` (must be globally unique, lowercase alphanumeric) |
| Cosmos cluster  | `ingest-db`              |
| Cosmos database | `ingest`                 |
| Container app   | `ingest`                 |
| Environment     | `ingest-env`             |
| Key vault       | `kv-ingest-prod`         |

Throughout the guide:

```powershell
$Rg          = "rg-ingest-prod"
$Location    = "westeurope"
$Acr         = "acringestprod"
$Env         = "ingest-env"
$App         = "ingest"
$Cluster     = "ingest-db"
$DbName      = "ingest"
$AdminUser   = "ingestadmin"
$AdminPwd    = (New-Guid).ToString()  # set once, store in Key Vault
$Vault       = "kv-ingest-prod"
$Image       = "$Acr.azurecr.io/ingest:1.0.0"
```

## Step 1 — Create the resource group

```powershell
az group create --name $Rg --location $Location
```

## Step 2 — Create the container registry

```powershell
az acr create --resource-group $Rg --name $Acr --sku Basic --admin-enabled false
```

`Basic` is the cheapest tier and is fine for a single image. `--admin-enabled false` forces us to authenticate via Entra ID / managed identity, which is the better long-term option.

## Step 3 — Build and push the image

From the repository root:

```powershell
# Build the multi-stage image (SPA + API, see /Dockerfile)
docker build -t $Image .

# Push to ACR
az acr login --name $Acr
docker push $Image
```

If you're building from CI, prefer:

```powershell
az acr build --registry $Acr --image ingest:1.0.0 .
```

— it skips the local Docker daemon and builds inside ACR's worker VMs.

## Step 4 — Provision Cosmos DB for MongoDB (vCore)

vCore is the variant whose wire protocol is MongoDB-compatible enough to run this code unmodified. We're not using the RU-based "Mongo API for Cosmos DB" because its feature surface is narrower.

```powershell
az cosmosdb mongocluster create `
    --resource-group $Rg `
    --cluster-name $Cluster `
    --location $Location `
    --administrator-user-name $AdminUser `
    --administrator-password $AdminPwd `
    --shard-node-tier "M30" `
    --shard-node-ha false `
    --shard-node-disk-size-gb 32 `
    --shard-node-count 1
```

`M30` is the smallest production-ish tier; pick whatever sizing matches your expected volume. HA off keeps cost low for non-critical environments — flip it on for production.

Allow your Container Apps environment to talk to the cluster:

```powershell
az cosmosdb mongocluster firewall-rule create `
    --resource-group $Rg `
    --cluster-name $Cluster `
    --rule-name "azure-services" `
    --start-ip-address 0.0.0.0 `
    --end-ip-address 0.0.0.0
```

The `0.0.0.0 → 0.0.0.0` range is Azure's convention for "allow Azure services". For tighter security, attach the cluster to a VNet and place the Container Apps environment in the same VNet (see [Microsoft's tutorial](https://learn.microsoft.com/azure/container-apps/networking)).

Grab the connection string:

```powershell
$MongoCs = az cosmosdb mongocluster list-connection-string `
    --resource-group $Rg `
    --cluster-name $Cluster `
    --query "connectionStrings[0].connectionString" -o tsv

# Append the database name and the cluster admin password
$MongoCs = $MongoCs.Replace("<user>", $AdminUser).Replace("<password>", $AdminPwd) + "/$DbName"
```

The result looks like `mongodb+srv://ingestadmin:<pwd>@ingest-db.mongocluster.cosmos.azure.com/ingest`.

> **URL-encode special characters.** The example password is a GUID, so it's URL-safe as-is. If you choose your own admin password containing characters like `@ : / ? # % &`, you **must** percent-encode it inside the connection string (e.g. `@` → `%40`), otherwise the driver mis-parses the host. The cleanest way to avoid this class of bug is to keep the auto-generated GUID password.

## Step 5 — Store secrets in Key Vault (recommended)

```powershell
az keyvault create --name $Vault --resource-group $Rg --location $Location
az keyvault secret set --vault-name $Vault --name "mongo-cs"        --value $MongoCs
az keyvault secret set --vault-name $Vault --name "api-key-pepper"  --value (New-Guid).Guid

# Optional but recommended: a known bootstrap admin key so you can sign in without
# reading the logs. Use a long, unique value — anyone who knows it has admin access.
$BootstrapKey = "admin." + [Convert]::ToBase64String([guid]::NewGuid().ToByteArray()).TrimEnd('=').Replace('+','-').Replace('/','_')
az keyvault secret set --vault-name $Vault --name "bootstrap-admin-key" --value $BootstrapKey
Write-Host "Bootstrap admin key (store it in your password manager): $BootstrapKey"
```

Using Key Vault is optional — you can plug values into Container Apps secrets directly — but recommended because rotation becomes a one-line `az keyvault secret set`.

## Step 6 — Create the Container Apps environment

```powershell
az provider register --namespace Microsoft.App
az provider register --namespace Microsoft.OperationalInsights

az containerapp env create `
    --name $Env `
    --resource-group $Rg `
    --location $Location
```

This implicitly creates a Log Analytics workspace and wires Container Apps logs into it. You'll see container stdout in `az containerapp logs show`.

## Step 7 — Create a user-assigned managed identity for the app

```powershell
$Identity = az identity create `
    --name "id-ingest" `
    --resource-group $Rg `
    --query id -o tsv

$IdentityClientId = az identity show --ids $Identity --query clientId -o tsv
$IdentityPrincipalId = az identity show --ids $Identity --query principalId -o tsv
```

Grant it pull rights on the ACR:

```powershell
$AcrId = az acr show --name $Acr --query id -o tsv
az role assignment create `
    --assignee $IdentityPrincipalId `
    --role "AcrPull" `
    --scope $AcrId
```

And `Key Vault Secrets User` if you're storing secrets there:

```powershell
$VaultId = az keyvault show --name $Vault --query id -o tsv
az role assignment create `
    --assignee $IdentityPrincipalId `
    --role "Key Vault Secrets User" `
    --scope $VaultId
```

## Step 8 — Create the container app

```powershell
az containerapp create `
    --name $App `
    --resource-group $Rg `
    --environment $Env `
    --image $Image `
    --user-assigned $Identity `
    --registry-server "$Acr.azurecr.io" `
    --registry-identity $Identity `
    --target-port 8080 `
    --ingress external `
    --min-replicas 1 `
    --max-replicas 3 `
    --cpu 0.5 --memory 1Gi `
    --secrets `
        "mongo-cs=keyvaultref:https://$Vault.vault.azure.net/secrets/mongo-cs,identityref:$Identity" `
        "api-key-pepper=keyvaultref:https://$Vault.vault.azure.net/secrets/api-key-pepper,identityref:$Identity" `
        "bootstrap-admin-key=keyvaultref:https://$Vault.vault.azure.net/secrets/bootstrap-admin-key,identityref:$Identity" `
    --env-vars `
        "ConnectionStrings__ingest=secretref:mongo-cs" `
        "ApiKey__Pepper=secretref:api-key-pepper" `
        "ApiKey__BootstrapAdminName=admin" `
        "ApiKey__BootstrapAdminKey=secretref:bootstrap-admin-key" `
        "Ingest__EnableSwagger=false" `
        "Ingest__CorsDevOrigins=[]" `
        "ASPNETCORE_FORWARDEDHEADERS_ENABLED=true"
```

What this does:

- Pulls `ingest:1.0.0` from ACR using the managed identity (no admin user, no password leak).
- Exposes port `8080` (the container's `ASPNETCORE_URLS`) behind an HTTPS ingress.
- Wires three **Key Vault-backed secrets** into the app — `mongo-cs` for `ConnectionStrings:ingest`, `api-key-pepper` for `ApiKey:Pepper`, and `bootstrap-admin-key` for `ApiKey:BootstrapAdminKey` so the first admin sign-in needs no log-scraping.
- Disables Swagger in production. Set `Ingest__EnableSwagger=true` if you want it on temporarily.
- Disables CORS — the SPA is served from the same origin as the API, so you don't need CORS in production at all.
- Enables forwarded-headers so URL generation knows the public hostname behind the Container Apps proxy.

If you didn't use Key Vault, replace the `--secrets` block with plain literals (`"mongo-cs=mongodb+srv://…"`). If you'd rather not pre-set an admin key at all, drop the `bootstrap-admin-key` secret and the `ApiKey__BootstrapAdminKey` env var — the app then generates a random key and logs it once (Step 10 covers that path).

### Optional — enable single sign-on (Microsoft / Google)

SSO is **off by default**; the app deploys and runs identically whether or not you set the keys below (see [architecture/authentication.md § Single sign-on](../architecture/authentication.md#single-sign-on-optional-second-scheme)). To turn it on, store the OAuth client id/secret as Key Vault secrets and project them onto the app — the non-secret provider shape (id, authority, scopes) already ships in the image's `appsettings.json`:

```powershell
az keyvault secret set --vault-name $Vault --name "ms-client-id"     --value "<entra-app-client-id>"
az keyvault secret set --vault-name $Vault --name "ms-client-secret" --value "<entra-app-client-secret>"

az containerapp update --name $App --resource-group $Rg `
    --set-secrets `
        "ms-client-id=keyvaultref:https://$Vault.vault.azure.net/secrets/ms-client-id,identityref:$Identity" `
        "ms-client-secret=keyvaultref:https://$Vault.vault.azure.net/secrets/ms-client-secret,identityref:$Identity" `
    --set-env-vars `
        "Sso__EnableSso=true" `
        "Sso__Providers__0__ClientId=secretref:ms-client-id" `
        "Sso__Providers__0__ClientSecret=secretref:ms-client-secret"
```

Then **register the production redirect URI** with the Entra app registration (or Google OAuth client):

```text
https://<host>/api/auth/callback/Microsoft
```

(`<host>` is the app FQDN from Step 9, or your custom domain. Use the matching provider id for others, e.g. `.../api/auth/callback/Google`.) Link each user's verified email to a `User`-kind account from the SPA before they can sign in — see [the admin user guide](../admin-user-guide/accounts.md).

## Step 9 — Grab the FQDN

```powershell
$Fqdn = az containerapp show --name $App --resource-group $Rg --query "properties.configuration.ingress.fqdn" -o tsv
# ingest.thankfulocean-…azurecontainerapps.io
```

Smoke-test it before opening a browser — the unauthenticated health endpoint should return `Healthy`:

```powershell
curl "https://$Fqdn/health"
```

Then open `https://$Fqdn/` in a browser: the admin SPA login screen should appear.

## Step 10 — Sign in with the bootstrap admin key

**If you set `bootstrap-admin-key` in Step 5** (recommended), you already have the key — it's the `$BootstrapKey` value you stored in your password manager. Paste it on the login screen. No log-scraping needed. Confirm it works from the CLI too:

```powershell
curl "https://$Fqdn/api/me" -H "X-Api-Key: $BootstrapKey"
```

**If you didn't pre-set a key**, the bootstrapper generated one and **logged it once** at `Warning` level. Read it from the Container Apps log stream:

```powershell
az containerapp logs show --name $App --resource-group $Rg --tail 200 | Select-String "Bootstrapped admin API key"
```

You'll see something like:

```
warn: Bootstrapped admin API key (shown only this once): abc123.xyz... .
      Use it in the X-Api-Key header. Set ApiKey:BootstrapAdminKey to avoid this next time,
      or rotate it via POST /api/admin/accounts/{Id}/keys then revoke this one.
```

Copy the value, then paste it on the login screen.

**Either way, rotate immediately afterwards:** in the SPA go to **Accounts → admin → Manage keys → Generate key**, copy the new value, then revoke the bootstrap one. Save the new key in your password manager. This matters especially when the bootstrap key came from configuration — it's only as secret as your Key Vault/config.

## Step 11 — Configure a custom domain (optional)

1. In the Azure portal, **Container App → Custom domains → Add**, follow the verification steps (TXT record, then CNAME).
2. Container Apps provisions a free managed certificate within a couple of minutes.
3. Update DNS at your registrar; once it resolves, the SPA is reachable at `https://ingest.yourdomain.org/`.

## Step 12 — Health probes (optional, recommended)

ASP.NET Core's default health endpoints are mapped at `/health` (readiness) and `/alive` (liveness) by `Ingest.ServiceDefaults`. Wire them into Container Apps:

```powershell
az containerapp update `
    --name $App --resource-group $Rg `
    --health-probe-method get `
    --liveness-probe-path /alive --liveness-probe-port 8080 `
    --readiness-probe-path /health --readiness-probe-port 8080
```

(If your `az` version is older and doesn't carry those flags, edit the app YAML through the portal: `Properties → Edit and deploy → Probes`.)

## Step 13 — Observability

- **Logs.** Already in Log Analytics through the Container Apps environment. Query with:
  ```kusto
  ContainerAppConsoleLogs_CL
  | where ContainerAppName_s == "ingest"
  | order by TimeGenerated desc
  ```
- **Metrics.** The Container App exposes CPU/memory/replica metrics in the Azure portal out of the box.
- **Distributed tracing.** The project uses OpenTelemetry through `Ingest.ServiceDefaults`. Wire it to Azure Monitor by setting the standard OTEL env vars:
  ```text
  OTEL_EXPORTER_OTLP_ENDPOINT=https://<your-collector-endpoint>
  OTEL_EXPORTER_OTLP_PROTOCOL=grpc
  ```
  Or pin Application Insights through a connection string:
  ```text
  APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=…
  ```

## Step 14 — CI/CD outline

A bare-bones GitHub Actions workflow:

```yaml
name: Deploy
on:
  push:
    branches: [main]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: azure/login@v2
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
      - run: az acr build --registry acringestprod --image ingest:${{ github.sha }} .
      - run: |
          az containerapp update \
            --name ingest --resource-group rg-ingest-prod \
            --image acringestprod.azurecr.io/ingest:${{ github.sha }}
```

For zero-downtime deployments, Container Apps automatically does revision-based rollout: the new image runs alongside the old one until it passes readiness probes.

If you'd rather publish the image to the **GitHub Container Registry** instead of ACR (handy for sharing a public image, or so others can run the [quickstart](quickstart.md) without building), this repo ships a manually-triggered workflow at [`.github/workflows/docker-image.yml`](../../.github/workflows/docker-image.yml). Run it from the **Actions** tab; it builds the `Dockerfile` and pushes `ghcr.io/<owner>/<repo>:<tag>` (plus the commit SHA) using the built-in `GITHUB_TOKEN`. Point `--image` / your container host at that registry instead of ACR if you go this route.

## Configuration reference

The settings the deployment commands above pass — and every other knob you can twist — are catalogued in [configuration.md](configuration.md). In particular:

- **Required in any production deployment:** `ConnectionStrings__ingest` and `ApiKey__Pepper`.
- **Recommended behind any reverse proxy:** `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.
- **Optional but useful:** the OpenTelemetry / Application Insights variables for telemetry.

## Free tier - a $0 evaluation deployment

You can run Ingest on Azure for **roughly $0** — no self-hosting, no paid tiers — by combining three free offerings. This is great for a persistent demo, a pilot, or light internal use; it is **not** a production setup (see the caveats at the end).

> **Microsoft 365 is not Azure.** A Microsoft 365 subscription does *not* include Azure compute or credits — it only gives you a free Microsoft Entra tenant for identity. To deploy anything here you still need a separate **Azure subscription** (the [Azure free account](https://azure.microsoft.com/free/) gives a time-limited credit plus always-free tiers, or use plain pay-as-you-go — you just won't be charged while you stay inside the free grants below).

What makes it free:

- **Compute — Azure Container Apps (Consumption), scaled to zero.** Every subscription gets a monthly free grant of 180,000 vCPU-seconds, 360,000 GiB-seconds, and 2 million requests. With `--min-replicas 0` the app costs nothing while idle and a low-traffic deployment stays inside the grant.
- **Database — Cosmos DB for MongoDB vCore Free Tier (M0).** A lifetime-free, dedicated 32 GB cluster, one per subscription. It speaks the same MongoDB wire protocol and username/password auth Ingest already uses.
- **Image — GitHub Container Registry (GHCR).** Built and pushed by the bundled [Build and publish Docker image](../../.github/workflows/docker-image.yml) workflow, so you skip Azure Container Registry (which has no free tier).

No Key Vault and no managed identity are needed: secrets go straight into the Container App and the public image is pulled anonymously.

### Variables

```powershell
$Rg           = "rg-ingest-free"
$Location     = "northeurope"      # must be a Free Tier region (see Step 2)
$Env          = "ingest-env"
$App          = "ingest"
$Cluster      = "ingest-db-free"
$DbName       = "ingest"
$AdminUser    = "ingestadmin"
$AdminPwd     = (New-Guid).ToString()                 # GUID = URL-safe, store it somewhere
$Image        = "ghcr.io/<owner>/<repo>:latest"       # your published GHCR image (lowercase)
$Pepper       = (New-Guid).Guid
$BootstrapKey = "admin." + [Convert]::ToBase64String([guid]::NewGuid().ToByteArray()).TrimEnd('=').Replace('+','-').Replace('/','_')
```

### Step F1 — Create the resource group

```powershell
az group create --name $Rg --location $Location
```

### Step F2 — Build and publish the image to GHCR

There's no ACR in this path. Publish the image with the bundled workflow instead:

1. In GitHub, open **Actions → Build and publish Docker image → Run workflow** (it's manual-only). It builds the `Dockerfile` and pushes `ghcr.io/<owner>/<repo>:latest` plus the commit SHA.
2. Make the package public so Container Apps can pull it without credentials: **your repo → Packages → the package → Package settings → Change visibility → Public**. (Prefer to keep it private? See the registry note in the caveats.)

Set `$Image` to the exact lowercase path shown on the package page.

### Step F3 — Provision the free Mongo cluster

The Free Tier is only offered in a limited set of regions — among them **East US, West US, West US 2, Central US, North Europe, France Central, Switzerland North, Australia East, Central India, Japan East**. Pick one for `$Location` (note `westeurope` is *not* on the list). Then:

```powershell
az cosmosdb mongocluster create `
    --resource-group $Rg `
    --cluster-name $Cluster `
    --location $Location `
    --administrator-user-name $AdminUser `
    --administrator-password $AdminPwd `
    --server-version 5.0 `
    --shard-node-tier "Free" `
    --shard-node-ha false `
    --shard-node-disk-size-gb 32 `
    --shard-node-count 1
```

`--shard-node-tier "Free"` is what selects the no-cost cluster; HA must be off and storage is fixed at 32 GB. (Newer Azure CLI versions rename the two credential flags to `--administrator-login` / `--administrator-login-password` — match your `az version`. The Free Tier also ignores premium disk types automatically.)

Allow Azure services to reach it, then build the connection string (same as the paid [Step 4](#step-4--provision-cosmos-db-for-mongodb-vcore)):

```powershell
az cosmosdb mongocluster firewall-rule create `
    --resource-group $Rg --cluster-name $Cluster `
    --rule-name "azure-services" `
    --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

$MongoCs = az cosmosdb mongocluster list-connection-string `
    --resource-group $Rg --cluster-name $Cluster `
    --query "connectionStrings[0].connectionString" -o tsv
$MongoCs = $MongoCs.Replace("<user>", $AdminUser).Replace("<password>", $AdminPwd) + "/$DbName"
```

### Step F4 — Create the Container Apps environment

```powershell
az provider register --namespace Microsoft.App
az provider register --namespace Microsoft.OperationalInsights

az containerapp env create --name $Env --resource-group $Rg --location $Location
```

This creates a Log Analytics workspace whose ingestion stays within its own free monthly grant at this volume.

### Step F5 — Create the container app (public image, scale-to-zero, plain secrets)

Container Apps requires you to register the registry **even for a public image**, then create the app:

```powershell
az containerapp registry set --name $App --resource-group $Rg --server ghcr.io

az containerapp create `
    --name $App `
    --resource-group $Rg `
    --environment $Env `
    --image $Image `
    --target-port 8080 `
    --ingress external `
    --min-replicas 0 --max-replicas 1 `
    --cpu 0.5 --memory 1Gi `
    --secrets `
        "mongo-cs=$MongoCs" `
        "api-key-pepper=$Pepper" `
        "bootstrap-admin-key=$BootstrapKey" `
    --env-vars `
        "ConnectionStrings__ingest=secretref:mongo-cs" `
        "ApiKey__Pepper=secretref:api-key-pepper" `
        "ApiKey__BootstrapAdminKey=secretref:bootstrap-admin-key" `
        "ApiKey__BootstrapAdminName=admin" `
        "Ingest__EnableSwagger=false" `
        "Ingest__CorsDevOrigins=[]" `
        "ASPNETCORE_FORWARDEDHEADERS_ENABLED=true"
```

`--min-replicas 0` is the key to staying free: the app scales to zero when idle and only consumes the grant while serving traffic. Secrets are stored directly on the app (no Key Vault), and `ApiKey__BootstrapAdminKey` means you can sign in immediately without reading the logs.

### Step F6 — Grab the URL, smoke-test, and sign in

```powershell
$Fqdn = az containerapp show --name $App --resource-group $Rg --query "properties.configuration.ingress.fqdn" -o tsv
curl "https://$Fqdn/health"                                   # expect: Healthy
curl "https://$Fqdn/api/me" -H "X-Api-Key: $BootstrapKey"     # confirms the admin key works
```

Open `https://$Fqdn/` and sign in with `$BootstrapKey`. The very first request after an idle period will be slow while the app cold-starts from zero — give it a few seconds and retry. Then rotate the bootstrap key as in [Step 10](#step-10--sign-in-with-the-bootstrap-admin-key).

### Free-tier caveats

This setup is for **evaluation and light use, not production**:

- **No HA, no SLA, no backup/restore** on the Free Tier cluster, and storage is capped at **32 GB**.
- **The cluster auto-pauses after 60 days of inactivity.** Data is retained — resume it from the portal/CLI — but a paused cluster will fail connections until resumed.
- **Cold starts.** With `--min-replicas 0`, the first request after idle waits for the container to spin up (seconds). Set `--min-replicas 1` for snappy responses, but that runs continuously and will consume the free grant faster (and bill once exceeded).
- **Free grants are per-subscription, per-calendar-month.** Heavy traffic that exceeds 180,000 vCPU-s / 360,000 GiB-s / 2M requests is billed at normal rates.
- **Region-limited.** Both the vCore Free Tier and your app must live in a supported region (Step F3).
- **Image visibility.** A public GHCR package pulls anonymously. To keep it private, supply a token with `read:packages` scope: `az containerapp registry set --name $App --resource-group $Rg --server ghcr.io --username <github-user> --password <token>`.

> **No-Azure fallback.** If the vCore Free Tier isn't available in a region you can use, a **MongoDB Atlas M0** cluster (512 MB, free forever, deployable into Azure regions) works just as well — point `ConnectionStrings__ingest` at its connection string and keep the rest of this section unchanged.

## Alternative hosting models

### Azure App Service (Linux containers)

Same image, slightly different plumbing:

1. Create an **App Service Plan** (Linux, `B1` or higher).
2. Create a **Web App for Containers** pointing at the same ACR.
3. Set **App settings** equivalent to the Container Apps env vars above. Use the `__` (double-underscore) form for nested keys — e.g. `ConnectionStrings__ingest`, `ApiKey__Pepper`, `ApiKey__BootstrapAdminKey`. (`:` works on Windows hosts but **not** in a Linux container's environment, so `__` is the portable choice.)
4. Enable **System-assigned managed identity** on the Web App and grant it `AcrPull` on the registry + `Key Vault Secrets User` on the vault.
5. Set the **container port** to `8080`.

App Service is cheaper for very small loads and has built-in deployment slots — useful if you want full blue/green at the app layer.

### AKS (Kubernetes)

If your org standardises on AKS, the `Dockerfile` produces an image that runs anywhere Linux ASP.NET Core does. A minimal `Deployment` + `Service` + `Ingress` works; secrets come from your usual flow (Azure Key Vault Provider for Secrets Store CSI Driver, Sealed Secrets, …). No special considerations — the app is stateless apart from MongoDB.

### Self-hosted MongoDB

If you prefer running MongoDB yourself (replica set on Azure VMs, MongoDB Atlas, …) point `ConnectionStrings:ingest` at it. The schema and indexes are managed by the app on startup (`MongoSetup.EnsureIndexesAsync`).

## Tearing it down

Cosmos DB vCore keeps billing whether or not anyone's using it, so delete everything when you're done evaluating. The simplest hammer is to drop the whole resource group:

```powershell
az group delete --name $Rg --yes --no-wait
```

That removes the Container App, ACR, Cosmos cluster, Key Vault, managed identity, and Log Analytics workspace in one go. (Key Vault may be retained in a *soft-deleted* state for the configured purge window; run `az keyvault purge --name $Vault` if you need to reuse the exact name immediately.) To pause cost without losing data instead, scale the app to zero (`az containerapp update --name $App --resource-group $Rg --min-replicas 0`) — note the Cosmos cluster still bills regardless.

## Operational checklist

Before you call it "production":

- [ ] `ApiKey:Pepper` is set to a long random value and stored in Key Vault.
- [ ] Bootstrap admin key has been **rotated** and the original revoked (especially if `ApiKey:BootstrapAdminKey` was pre-set).
- [ ] Cosmos DB shard-node-ha is **on** if you can't tolerate planned maintenance.
- [ ] `Ingest:EnableSwagger` is `false`.
- [ ] Liveness/readiness probes are wired.
- [ ] Logs flow to Log Analytics or Application Insights.
- [ ] You've validated PowerBI can connect with an Operator-role key (see [powerbi.md](powerbi.md)).
- [ ] **If using SSO:** `Sso__EnableSso=true`, the client id/secret are sourced from Key Vault (not inline), the production redirect URI is registered with each IdP, and at least one admin's identity is linked to a `User` account (so you're not locked out if a key is lost).
- [ ] A backup of `mongo-cs` (your Mongo connection string) is stored somewhere you can find without logging into Azure.

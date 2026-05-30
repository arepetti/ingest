# Hosting

This guide explains how to host Ingest in **Microsoft Azure** end-to-end. The recommended target is **Azure Container Apps** backed by **Cosmos DB for MongoDB (vCore)** — it's the cheapest production-ready option and avoids running your own MongoDB cluster. Alternatives at the bottom for self-hosted MongoDB, App Service, or AKS.

The Ingest container is single-image: it bundles the compiled API **and** the built admin SPA. You don't need a separate static-web-app deployment.

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
- The [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) installed and logged in (`az login`).
- Docker (or a CI pipeline that can do `docker build`).
- A clone of this repository, including the `Dockerfile` at the root.

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

## Step 5 — Store secrets in Key Vault (recommended)

```powershell
az keyvault create --name $Vault --resource-group $Rg --location $Location
az keyvault secret set --vault-name $Vault --name "mongo-cs"        --value $MongoCs
az keyvault secret set --vault-name $Vault --name "api-key-pepper"  --value (New-Guid).Guid
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
    --env-vars `
        "ConnectionStrings__ingest=secretref:mongo-cs" `
        "ApiKey__Pepper=secretref:api-key-pepper" `
        "ApiKey__BootstrapAdminName=admin" `
        "Ingest__EnableSwagger=false" `
        "Ingest__CorsDevOrigins=[]" `
        "ASPNETCORE_FORWARDEDHEADERS_ENABLED=true"
```

What this does:

- Pulls `ingest:1.0.0` from ACR using the managed identity (no admin user, no password leak).
- Exposes port `8080` (the container's `ASPNETCORE_URLS`) behind an HTTPS ingress.
- Wires two **Key Vault-backed secrets** into the app — `mongo-cs` for `ConnectionStrings:ingest` and `api-key-pepper` for `ApiKey:Pepper`.
- Disables Swagger in production. Set `Ingest__EnableSwagger=true` if you want it on temporarily.
- Disables CORS — the SPA is served from the same origin as the API, so you don't need CORS in production at all.
- Enables forwarded-headers so URL generation knows the public hostname behind the Container Apps proxy.

If you didn't use Key Vault, replace the `--secrets` block with plain literals (`"mongo-cs=mongodb+srv://…"`).

## Step 9 — Grab the FQDN

```powershell
az containerapp show --name $App --resource-group $Rg --query "properties.configuration.ingress.fqdn" -o tsv
# ingest.thankfulocean-…azurecontainerapps.io
```

Open it in a browser: the admin SPA login screen should appear immediately.

## Step 10 — Retrieve the bootstrap admin key

The bootstrap admin is created automatically on first start and the plaintext key is **logged once** at `Warning` level. Read it from the Container Apps log stream:

```powershell
az containerapp logs show --name $App --resource-group $Rg --tail 200 | Select-String "Bootstrapped admin API key"
```

You'll see something like:

```
warn: Bootstrapped admin API key (shown only this once): abc123.xyz... .
      Use it in the X-Api-Key header. Rotate it via POST /api/admin/accounts/{Id}/keys, then revoke this one.
```

Copy the value, then paste it on the login screen.

**Immediately afterwards**, rotate it: in the SPA go to **Accounts → admin → Manage keys → Generate key**, copy the new value, then revoke the bootstrap one. Save the new key in your password manager.

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

## Configuration reference

The settings the deployment commands above pass — and every other knob you can twist — are catalogued in [configuration.md](configuration.md). In particular:

- **Required in any production deployment:** `ConnectionStrings__ingest` and `ApiKey__Pepper`.
- **Recommended behind any reverse proxy:** `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.
- **Optional but useful:** the OpenTelemetry / Application Insights variables for telemetry.

## Alternative hosting models

### Azure App Service (Linux containers)

Same image, slightly different plumbing:

1. Create an **App Service Plan** (Linux, `B1` or higher).
2. Create a **Web App for Containers** pointing at the same ACR.
3. Set **App settings** equivalent to the Container Apps env vars above (App Service uses `:` rather than `__` for nesting in env vars; the .NET runtime accepts both).
4. Enable **System-assigned managed identity** on the Web App and grant it `AcrPull` on the registry + `Key Vault Secrets User` on the vault.
5. Set the **container port** to `8080`.

App Service is cheaper for very small loads and has built-in deployment slots — useful if you want full blue/green at the app layer.

### AKS (Kubernetes)

If your org standardises on AKS, the `Dockerfile` produces an image that runs anywhere Linux ASP.NET Core does. A minimal `Deployment` + `Service` + `Ingress` works; secrets come from your usual flow (Azure Key Vault Provider for Secrets Store CSI Driver, Sealed Secrets, …). No special considerations — the app is stateless apart from MongoDB.

### Self-hosted MongoDB

If you prefer running MongoDB yourself (replica set on Azure VMs, MongoDB Atlas, …) point `ConnectionStrings:ingest` at it. The schema and indexes are managed by the app on startup (`MongoSetup.EnsureIndexesAsync`).

## Operational checklist

Before you call it "production":

- [ ] `ApiKey:Pepper` is set to a long random value and stored in Key Vault.
- [ ] Bootstrap admin key has been **rotated** and the original revoked.
- [ ] Cosmos DB shard-node-ha is **on** if you can't tolerate planned maintenance.
- [ ] `Ingest:EnableSwagger` is `false`.
- [ ] Liveness/readiness probes are wired.
- [ ] Logs flow to Log Analytics or Application Insights.
- [ ] You've validated PowerBI can connect with an Operator-role key (see [powerbi.md](powerbi.md)).
- [ ] A backup of `mongo-cs` (your Mongo connection string) is stored somewhere you can find without logging into Azure.

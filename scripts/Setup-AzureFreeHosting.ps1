<#
.SYNOPSIS
    One-shot deployment of Ingest to Azure Container Apps + Cosmos DB for MongoDB vCore
    (Free Tier), with a choice of container registry.

.DESCRIPTION
    Builds *this* checkout of Ingest (so any local config / code changes are included),
    publishes the image, provisions the free-tier Azure footprint described in
    docs/setup/hosting.md (§ "Free tier - a $0 evaluation deployment"), deploys the app,
    and prints the public URL plus the admin key you sign in with.

    At the start you choose how the image is stored:

      1) GHCR  - GitHub Container Registry. Genuinely FREE (~$0 total for light/eval use).
                 Builds the image locally with Docker and pushes it to your GHCR namespace.
                 Requires: Docker + a GitHub Personal Access Token with 'write:packages'.

      2) ACR   - Azure Container Registry (Basic). NOT free - ACR Basic adds a small
                 recurring charge (~$5/month) - but needs no Docker and no GitHub: the image
                 is built in the cloud with 'az acr build' and pulled via managed identity.

    Either way the compute (Container Apps, scaled to zero) and database (Cosmos vCore Free
    Tier) stay within free grants; only the ACR option adds the registry charge.

    Interactive by default: it scans for the tools it needs, prints what it intends to
    install, asks for confirmation, signs in, asks for the two secrets that MUST change
    before any deployment (the API-key pepper and the bootstrap admin key), shows the full
    plan, and only then builds and deploys.

    Pass -Yes to accept every default / confirmation (non-interactive), -Registry to pick
    the registry up front, and override any name or credential with the matching parameter.

    Tooling:
      - .NET SDK (`dotnet`)  - assumed present; only verified on PATH and used for a quick
                               pre-flight compile of your local changes.
      - Azure CLI (`az`)     - installed via winget if missing.
      - Docker               - only required for the GHCR option; the script can install
                               Docker Desktop via winget and start it.

.NOTES
    Run from anywhere; the script locates the repo root relative to its own location.
    Tear everything down again with:  az group delete --name <ResourceGroup> --yes
#>

[CmdletBinding()]
param(
    [ValidateSet('ghcr', 'acr')]
    [string]$Registry,                             # 'ghcr' (free) or 'acr'; prompted when empty
    [string]$Location          = 'northeurope',    # must be a Cosmos vCore Free Tier region
    [string]$ResourceGroup     = 'rg-ingest-free',
    [string]$AppName           = 'ingest',
    [string]$EnvName           = 'ingest-env',
    [string]$ClusterName,                          # globally unique; auto-generated when empty
    [string]$DbName            = 'ingest',
    [string]$CosmosAdminUser   = 'ingestadmin',
    [string]$AcrName,                              # ACR option: globally unique; auto-generated when empty
    [string]$GitHubUser,                           # GHCR option: namespace / login; prompted when empty
    [string]$GitHubToken,                          # GHCR option: PAT with write:packages; prompted (hidden) when empty
    [string]$ImageRepository   = 'ingest',         # GHCR option: package name under your GHCR namespace
    [string]$Pepper,                               # ApiKey:Pepper - prompted (auto-generated) when empty
    [string]$BootstrapAdminKey,                    # ApiKey:BootstrapAdminKey - prompted (auto-generated) when empty
    [string]$Subscription,                         # subscription id or name; uses the current default when empty
    [string]$ImageTag,                             # image tag; defaults to a timestamp
    [switch]$SkipLocalBuild,                       # skip the pre-flight `dotnet build` of your changes
    [switch]$Yes                                   # non-interactive: accept all defaults & confirmations
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Cosmos DB for MongoDB vCore Free Tier is only offered in these regions (docs/setup/hosting.md § Step F3).
$FreeTierRegions = @(
    'eastus', 'westus', 'westus2', 'centralus', 'northeurope',
    'francecentral', 'switzerlandnorth', 'australiaeast', 'centralindia', 'japaneast'
)

#--------------------------------------------------------------------------------------
# Small helpers
#--------------------------------------------------------------------------------------

function Write-Section([string]$Title) {
    Write-Host ''
    Write-Host ('=' * 72) -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host ('=' * 72) -ForegroundColor Cyan
}

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host ">> $Message" -ForegroundColor Green
}

function Write-Info([string]$Message)  { Write-Host "   $Message" -ForegroundColor Gray }
function Write-Warn2([string]$Message)  { Write-Host "   ! $Message" -ForegroundColor Yellow }

function Read-WithDefault([string]$Prompt, [string]$Default) {
    if ($Yes) { return $Default }
    $value = Read-Host ("{0} [{1}]" -f $Prompt, $Default)
    if ([string]::IsNullOrWhiteSpace($value)) { return $Default }
    return $value.Trim()
}

function Confirm-Continue([string]$Prompt) {
    if ($Yes) { return $true }
    $answer = Read-Host "$Prompt [y/N]"
    return ($answer -match '^(y|yes)$')
}

function Read-Secret([string]$Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function New-RandomToken {
    param([int]$Bytes = 24, [string]$Prefix = '')
    $buffer = New-Object 'byte[]' $Bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
    $token = [Convert]::ToBase64String($buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    return "$Prefix$token"
}

function New-NameSuffix {
    -join ((48..57) + (97..122) | Get-Random -Count 6 | ForEach-Object { [char]$_ })
}

function Test-CommandExists([string]$Name) {
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

# Run an `az` command, echoing it first, and throw on a non-zero exit code.
# Use -Sensitive to suppress the echo for commands that carry secrets.
# Use -Capture to return stdout (trimmed) instead of streaming it.
function Invoke-Az {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$Sensitive,
        [switch]$Capture
    )
    if (-not $Sensitive) {
        Write-Host "   az $($Arguments -join ' ')" -ForegroundColor DarkGray
    } else {
        Write-Host "   az $($Arguments[0]) $($Arguments[1]) ... (arguments hidden - they contain secrets)" -ForegroundColor DarkGray
    }

    if ($Capture) {
        $output = & az @Arguments 2>&1
    } else {
        & az @Arguments
        $output = $null
    }

    if ($LASTEXITCODE -ne 0) {
        if ($Capture -and $output) { Write-Host ($output -join [Environment]::NewLine) -ForegroundColor Red }
        throw "Azure CLI command failed (exit code $LASTEXITCODE): az $($Arguments[0]) $($Arguments[1])"
    }
    if ($Capture) { return ($output | Out-String).Trim() }
}

# Wait for the Docker engine to be reachable, starting Docker Desktop if necessary.
function Wait-DockerEngine {
    & docker info 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { return }

    $dockerDesktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (-not (Test-Path $dockerDesktop)) {
        throw "The Docker engine isn't running and 'Docker Desktop.exe' was not found. Start Docker, wait for 'Engine running', and re-run."
    }

    Write-Info 'Docker engine is not running yet; starting Docker Desktop (first launch can take a minute)...'
    Start-Process $dockerDesktop | Out-Null
    for ($i = 1; $i -le 40; $i++) {
        Start-Sleep -Seconds 3
        & docker info 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { Write-Info 'Docker engine is ready.'; return }
        if ($i % 5 -eq 0) { Write-Info "Still waiting for the Docker engine... ($($i * 3)s)" }
    }
    throw "Docker still isn't ready. Once Docker Desktop shows 'Engine running', re-run this script."
}

#--------------------------------------------------------------------------------------
# Main
#--------------------------------------------------------------------------------------

try {
    $RepoRoot   = Split-Path -Parent $PSScriptRoot
    $Dockerfile = Join-Path $RepoRoot 'Dockerfile'

    Write-Section 'Ingest - Azure deployment'
    Write-Host @"
   This script deploys Ingest to Azure Container Apps (scale-to-zero) backed by a
   Cosmos DB for MongoDB vCore Free Tier cluster, as in docs/setup/hosting.md. The image
   is built from THIS checkout, so your local config and code changes are included, and
   the two insecure default secrets - the API-key pepper and the bootstrap admin key -
   are set during this run.

   Repository root: $RepoRoot
"@ -ForegroundColor Gray

    if (-not (Test-Path $Dockerfile)) {
        throw "Could not find the Dockerfile at '$Dockerfile'. Run this script from within the Ingest repository (it lives in /scripts)."
    }

    #----------------------------------------------------------------------------------
    # 1. Choose the container registry
    #----------------------------------------------------------------------------------
    Write-Section 'Step 1 - Choose how the image is stored'
    Write-Host @"
   1) GHCR - GitHub Container Registry.  FREE (~`$0 total for light/eval use).
             * Builds the image locally with Docker and pushes it to your GHCR namespace.
             * Requires Docker and a GitHub token (write:packages).

   2) ACR  - Azure Container Registry (Basic).  NOT free (~`$5/month for the registry).
             * Builds the image in the cloud with 'az acr build' - no Docker, no GitHub.
             * Pulled by the app via a managed identity.

   In both cases the compute and database stay within Azure's free grants; only ACR adds
   the registry charge, so it is NOT a `$0 deployment.
"@ -ForegroundColor Gray

    if (-not $Registry) {
        if ($Yes) {
            $Registry = 'ghcr'
        } else {
            $choice = Read-Host '   Choose 1 (GHCR, free) or 2 (ACR) [1]'
            $Registry = if ($choice -eq '2') { 'acr' } else { 'ghcr' }
        }
    }
    $useGhcr = ($Registry -eq 'ghcr')
    Write-Info ("Selected registry: {0}" -f ($(if ($useGhcr) { 'GHCR (free)' } else { 'ACR (~$5/month)' })))

    #----------------------------------------------------------------------------------
    # 2. Prerequisite scan
    #----------------------------------------------------------------------------------
    Write-Section 'Step 2 - Checking prerequisites'

    # dotnet is assumed present; we only verify it is reachable.
    if (Test-CommandExists 'dotnet') {
        $dotnetVersion = (& dotnet --version) 2>$null
        Write-Info "Found .NET SDK: $dotnetVersion"
    } else {
        throw "The .NET SDK ('dotnet') is required but was not found on PATH. Install it from https://dotnet.microsoft.com/download and re-run."
    }

    # Build a list of things we need to install, then ask once before touching anything.
    $toInstall = @()

    # Docker is only needed for the GHCR (local build) path.
    $dockerPresent = $true
    if ($useGhcr) {
        $dockerPresent = Test-CommandExists 'docker'
        if (-not $dockerPresent) {
            $toInstall += 'Docker Desktop (winget package "Docker.DockerDesktop")'
        } else {
            Write-Info "Found Docker: $((& docker --version) 2>$null)"
        }
    }

    $azPresent = Test-CommandExists 'az'
    if (-not $azPresent) {
        $toInstall += 'Azure CLI (winget package "Microsoft.AzureCLI")'
    } else {
        Write-Info "Found Azure CLI: $((& az version --query '\"azure-cli\"' -o tsv) 2>$null)"
    }

    # The Container Apps commands live in an extension that may not be installed yet.
    $containerappExtPresent = $false
    if ($azPresent) {
        & az extension show --name containerapp 2>$null | Out-Null
        $containerappExtPresent = ($LASTEXITCODE -eq 0)
    }
    if (-not $containerappExtPresent) {
        $toInstall += "Azure CLI 'containerapp' extension"
    } else {
        Write-Info "Found Azure CLI extension: containerapp"
    }

    $dockerWasInstalledNow = $false
    if ($toInstall.Count -gt 0) {
        Write-Step 'The following need to be installed before we can continue:'
        $toInstall | ForEach-Object { Write-Host "     - $_" -ForegroundColor Yellow }

        if (-not (Confirm-Continue 'Install the items listed above now?')) {
            throw 'Cannot continue without the required tooling. Nothing was installed.'
        }

        $hasWinget = Test-CommandExists 'winget'

        if ($useGhcr -and -not $dockerPresent) {
            if (-not $hasWinget) {
                throw "winget is not available, so Docker cannot be installed automatically. Install Docker Desktop from https://www.docker.com/products/docker-desktop/ and re-run."
            }
            Write-Step 'Installing Docker Desktop via winget (this can take a few minutes)...'
            winget install -e --id Docker.DockerDesktop --accept-package-agreements --accept-source-agreements
            if ($LASTEXITCODE -ne 0) {
                throw "winget could not install Docker Desktop. Install it by hand from https://www.docker.com/products/docker-desktop/ and re-run."
            }
            $dockerWasInstalledNow = $true
        }

        if (-not $azPresent) {
            if (-not $hasWinget) {
                throw "winget is not available, so the Azure CLI cannot be installed automatically. Install it from https://learn.microsoft.com/cli/azure/install-azure-cli and re-run."
            }
            Write-Step 'Installing the Azure CLI via winget (this can take a few minutes)...'
            winget install -e --id Microsoft.AzureCLI --accept-package-agreements --accept-source-agreements
            if ($LASTEXITCODE -ne 0) {
                throw "winget could not install the Azure CLI. Install it by hand from https://learn.microsoft.com/cli/azure/install-azure-cli and re-run."
            }
            $azBin = Join-Path $env:ProgramFiles 'Microsoft SDKs\Azure\CLI2\wbin'
            if ((Test-Path $azBin) -and ($env:Path -notlike "*$azBin*")) {
                $env:Path = "$azBin;$env:Path"
            }
            if (-not (Test-CommandExists 'az')) {
                throw "The Azure CLI was installed but 'az' is still not on PATH in this session. Open a NEW terminal and run this script again."
            }
            Write-Info "Azure CLI installed: $((& az version --query '\"azure-cli\"' -o tsv) 2>$null)"
        }

        if ($dockerWasInstalledNow) {
            Write-Host ''
            Write-Warn2 'Docker Desktop was just installed. Windows requires a few manual steps for the first launch:'
            Write-Warn2 '   1. Open "Docker Desktop" from the Start menu.'
            Write-Warn2 '   2. Accept the terms; it may ask you to sign out / restart.'
            Write-Warn2 '   3. Wait until it shows "Engine running".'
            Write-Warn2 '   4. Run this script again to continue the deployment.'
            return
        }

        if (-not $containerappExtPresent) {
            Write-Step "Installing the Azure CLI 'containerapp' extension..."
            Invoke-Az -Arguments @('extension', 'add', '--name', 'containerapp', '--upgrade')
        }
    } else {
        Write-Info 'All required tooling is already present.'
    }

    if ($useGhcr) {
        Write-Step 'Verifying the Docker engine is running...'
        Wait-DockerEngine
    }

    #----------------------------------------------------------------------------------
    # 3. Azure login & subscription
    #----------------------------------------------------------------------------------
    Write-Section 'Step 3 - Azure sign-in'

    & az account show 2>$null | Out-Null
    $loggedIn = ($LASTEXITCODE -eq 0)

    if ($loggedIn) {
        $currentAccount = (& az account show --query 'name' -o tsv) 2>$null
        Write-Info "Already signed in. Active subscription: $currentAccount"
        if (-not $Yes -and -not (Confirm-Continue 'Use this account / sign-in?')) {
            $loggedIn = $false
        }
    }

    if (-not $loggedIn) {
        $useDeviceCode = $false
        if (-not $Yes) {
            Write-Host '   How would you like to sign in?' -ForegroundColor Gray
            Write-Host '     1) Open a browser window (default)' -ForegroundColor Gray
            Write-Host '     2) Device code (shows a URL + code to enter on another device)' -ForegroundColor Gray
            $choice = Read-Host '   Choose 1 or 2 [1]'
            $useDeviceCode = ($choice -eq '2')
        }
        Write-Step 'Signing in to Azure...'
        if ($useDeviceCode) {
            Invoke-Az -Arguments @('login', '--use-device-code')
        } else {
            Invoke-Az -Arguments @('login')
        }
    }

    if ($Subscription) {
        Write-Step "Selecting subscription '$Subscription'..."
        Invoke-Az -Arguments @('account', 'set', '--subscription', $Subscription)
    } elseif (-not $Yes) {
        $subName = (& az account show --query 'name' -o tsv) 2>$null
        $subId   = (& az account show --query 'id' -o tsv) 2>$null
        Write-Info "Current subscription: $subName ($subId)"
        if (-not (Confirm-Continue 'Deploy into this subscription?')) {
            Write-Host '   Available subscriptions:' -ForegroundColor Gray
            & az account list --query '[].{Name:name, Id:id}' -o table
            $picked = Read-Host '   Enter the subscription name or id to use'
            if (-not [string]::IsNullOrWhiteSpace($picked)) {
                Invoke-Az -Arguments @('account', 'set', '--subscription', $picked.Trim())
            }
        }
    }

    #----------------------------------------------------------------------------------
    # 4. GitHub Container Registry sign-in (GHCR option only)
    #----------------------------------------------------------------------------------
    $GhcrOwner = $null
    if ($useGhcr) {
        Write-Section 'Step 4 - GitHub Container Registry (GHCR) sign-in'
        Write-Host @"
   The image is published to GHCR under your GitHub account. You need:
     * your GitHub username, and
     * a Personal Access Token (classic) with the 'write:packages' scope.
       Create one at: https://github.com/settings/tokens
       ('write:packages' also grants the 'read:packages' the running app uses to pull.)

   The package is created PRIVATE and the app pulls it with these same credentials,
   so nothing has to be made public.
"@ -ForegroundColor Gray

        if ([string]::IsNullOrWhiteSpace($GitHubUser)) {
            if ($Yes) { throw 'GitHub username is required for GHCR. Pass -GitHubUser when running with -Yes.' }
            $GitHubUser = (Read-Host '   GitHub username').Trim()
        }
        if ([string]::IsNullOrWhiteSpace($GitHubUser)) { throw 'A GitHub username is required.' }

        if ([string]::IsNullOrWhiteSpace($GitHubToken)) {
            if ($Yes) { throw 'GitHub token is required for GHCR. Pass -GitHubToken when running with -Yes.' }
            $GitHubToken = Read-Secret '   GitHub Personal Access Token (write:packages, hidden)'
        }
        if ([string]::IsNullOrWhiteSpace($GitHubToken)) { throw 'A GitHub Personal Access Token is required.' }

        $GhcrOwner = $GitHubUser.ToLowerInvariant()
        Write-Step 'Logging in to ghcr.io with Docker...'
        $GitHubToken | & docker login ghcr.io --username $GitHubUser --password-stdin
        if ($LASTEXITCODE -ne 0) {
            throw "Docker could not sign in to ghcr.io. Check the username and that the token has the 'write:packages' scope."
        }
        Write-Info 'Signed in to ghcr.io.'
    }

    #----------------------------------------------------------------------------------
    # 5. The two secrets that MUST change before a deployment
    #----------------------------------------------------------------------------------
    Write-Section 'Step 5 - Required secrets (these must change before any deployment)'
    Write-Host @"
   Ingest ships with two insecure default values in its configuration. Both MUST be
   replaced before deploying:

     * ApiKey:Pepper            (default "dev-pepper-change-me")
                                a server-wide secret that hardens stored API-key hashes.
     * ApiKey:BootstrapAdminKey (default "localdev.local-dev-admin-key-change-me")
                                the admin API key you sign in with the first time.

   Press Enter at either prompt to have a strong value generated for you.
"@ -ForegroundColor Gray

    if ([string]::IsNullOrWhiteSpace($Pepper)) {
        $entered = if ($Yes) { '' } else { Read-Host '   API-key pepper (Enter = auto-generate)' }
        if ([string]::IsNullOrWhiteSpace($entered)) {
            $Pepper = New-RandomToken -Bytes 32
            Write-Info 'Generated a random pepper.'
        } else {
            $Pepper = $entered.Trim()
        }
    }

    if ([string]::IsNullOrWhiteSpace($BootstrapAdminKey)) {
        $entered = if ($Yes) { '' } else { Read-Host '   Bootstrap admin key (Enter = auto-generate)' }
        if ([string]::IsNullOrWhiteSpace($entered)) {
            $BootstrapAdminKey = New-RandomToken -Bytes 24 -Prefix 'admin.'
            Write-Info 'Generated a random bootstrap admin key.'
        } else {
            $BootstrapAdminKey = $entered.Trim()
        }
    }

    #----------------------------------------------------------------------------------
    # 6. Names & region
    #----------------------------------------------------------------------------------
    Write-Section 'Step 6 - Deployment names & region'

    $Location = (Read-WithDefault 'Azure region' $Location).ToLowerInvariant()
    if ($FreeTierRegions -notcontains $Location) {
        Write-Warn2 "'$Location' is not in the Cosmos vCore Free Tier region list:"
        Write-Warn2 ($FreeTierRegions -join ', ')
        Write-Warn2 "The free Mongo cluster will likely fail to create here."
        if (-not (Confirm-Continue 'Continue anyway?')) { throw 'Aborted: pick a Free Tier region.' }
    }

    $ResourceGroup = Read-WithDefault 'Resource group'  $ResourceGroup
    $EnvName       = Read-WithDefault 'Container Apps environment name' $EnvName
    $AppName       = Read-WithDefault 'Container app name' $AppName
    $DbName        = Read-WithDefault 'Mongo database name' $DbName

    $suffix = New-NameSuffix
    if ([string]::IsNullOrWhiteSpace($ClusterName)) { $ClusterName = "ingest-db-$suffix" }
    $ClusterName = (Read-WithDefault 'Cosmos Mongo cluster name (globally unique)' $ClusterName).ToLowerInvariant()

    if ([string]::IsNullOrWhiteSpace($ImageTag)) { $ImageTag = Get-Date -Format 'yyyyMMddHHmmss' }

    if ($useGhcr) {
        $ImageRepository = (Read-WithDefault 'GHCR package name' $ImageRepository).ToLowerInvariant()
        $LocalImageRef   = "ghcr.io/$GhcrOwner/$($ImageRepository):$ImageTag"
        $FullImage       = $LocalImageRef
    } else {
        if ([string]::IsNullOrWhiteSpace($AcrName)) { $AcrName = "ingestacr$suffix" }
        $AcrName       = (Read-WithDefault 'Container registry name (globally unique, lowercase alphanumeric)' $AcrName).ToLowerInvariant()
        $LocalImageRef = "ingest:$ImageTag"
        $FullImage     = "$AcrName.azurecr.io/$LocalImageRef"
    }

    $IdentityName   = 'id-ingest'
    $CosmosAdminPwd = [guid]::NewGuid().ToString()   # GUID = URL-safe; no manual percent-encoding needed

    #----------------------------------------------------------------------------------
    # 7. Plan & confirm
    #----------------------------------------------------------------------------------
    Write-Section 'Step 7 - Review the plan'

    if ($useGhcr) {
        $registryLine = "GHCR package ............. $FullImage  (private)"
        $registryPull = "GHCR pull credentials .... (your GitHub user + token)"
        $costLine     = "Cost: GHCR + Container Apps (scaled to zero) + Cosmos Free Tier = roughly `$0 for light/eval use."
    } else {
        $registryLine = "Container registry (ACR) . $AcrName  (Basic SKU - small recurring cost)`n     Built image .............. $FullImage"
        $registryPull = "Image pull ............... via managed identity '$IdentityName' (AcrPull)"
        $costLine     = "Cost: Container Apps (scaled to zero) + Cosmos Free Tier are free for light use; ACR Basic adds ~`$5/month."
    }

    Write-Host @"
   Registry option: $(if ($useGhcr) { 'GHCR (free)' } else { 'ACR (~$5/month)' })

   The following will be created in subscription
   '$((& az account show --query 'name' -o tsv) 2>$null)':

     Resource group ........... $ResourceGroup        ($Location)
     $registryLine
     Container Apps env ....... $EnvName
     Container app ............ $AppName              (min-replicas 0, scale-to-zero)
     Cosmos Mongo (Free Tier) . $ClusterName          (db: $DbName, admin: $CosmosAdminUser)

   Application secrets wired into the app:
     ApiKey:Pepper ............ (set)
     ApiKey:BootstrapAdminKey . $BootstrapAdminKey
     Mongo connection string .. (generated from the new cluster)
     $registryPull

   $costLine
   Tear it all down later with:  az group delete --name $ResourceGroup --yes
"@ -ForegroundColor Gray

    if (-not (Confirm-Continue 'Proceed with the deployment?')) {
        Write-Host ''
        Write-Host 'Aborted. No Azure resources were created.' -ForegroundColor Yellow
        return
    }

    #----------------------------------------------------------------------------------
    # 8. Pre-flight: compile the local changes
    #----------------------------------------------------------------------------------
    if (-not $SkipLocalBuild) {
        Write-Section 'Step 8 - Pre-flight build of your local changes'
        Write-Info 'Compiling Ingest.Api (catches code/config errors before the slower image build)...'
        $apiProject = Join-Path $RepoRoot 'src/Ingest.Api/Ingest.Api.csproj'
        & dotnet build $apiProject -c Release --nologo
        if ($LASTEXITCODE -ne 0) {
            throw 'Local build failed. Fix the build errors above (or pass -SkipLocalBuild to bypass) and re-run.'
        }
        Write-Info 'Local build succeeded.'
    } else {
        Write-Warn2 'Skipping the local pre-flight build (-SkipLocalBuild).'
    }

    #----------------------------------------------------------------------------------
    # 9. Provision the foundation (providers + resource group)
    #----------------------------------------------------------------------------------
    Write-Section 'Step 9 - Provisioning Azure'

    Write-Step 'Registering resource providers...'
    Invoke-Az -Arguments @('provider', 'register', '--namespace', 'Microsoft.App')
    Invoke-Az -Arguments @('provider', 'register', '--namespace', 'Microsoft.OperationalInsights')
    Invoke-Az -Arguments @('provider', 'register', '--namespace', 'Microsoft.DocumentDB')

    Write-Step "Creating resource group '$ResourceGroup'..."
    Invoke-Az -Arguments @('group', 'create', '--name', $ResourceGroup, '--location', $Location)

    #----------------------------------------------------------------------------------
    # 10. Build & publish the image
    #----------------------------------------------------------------------------------
    if ($useGhcr) {
        Write-Step "Building $FullImage locally (multi-stage Dockerfile: SPA + API)..."
        & docker build -t $FullImage -f $Dockerfile $RepoRoot
        if ($LASTEXITCODE -ne 0) { throw 'Docker build failed. See the output above.' }

        Write-Step 'Pushing the image to GHCR...'
        & docker push $FullImage
        if ($LASTEXITCODE -ne 0) { throw 'Docker push to GHCR failed. See the output above.' }
        Write-Info 'Image published.'
    } else {
        Write-Step "Creating container registry '$AcrName'..."
        Invoke-Az -Arguments @('acr', 'create', '--resource-group', $ResourceGroup, '--name', $AcrName, '--sku', 'Basic', '--admin-enabled', 'false')

        Write-Step 'Building the image in the cloud with az acr build (no local Docker needed)...'
        Push-Location $RepoRoot
        try {
            Invoke-Az -Arguments @('acr', 'build', '--registry', $AcrName, '--image', $LocalImageRef, '.')
        } finally {
            Pop-Location
        }
    }

    #----------------------------------------------------------------------------------
    # 11. Container Apps environment
    #----------------------------------------------------------------------------------
    Write-Step "Creating the Container Apps environment '$EnvName'..."
    Invoke-Az -Arguments @('containerapp', 'env', 'create', '--name', $EnvName, '--resource-group', $ResourceGroup, '--location', $Location)

    #----------------------------------------------------------------------------------
    # 12. Managed identity + AcrPull (ACR option only)
    #----------------------------------------------------------------------------------
    $identityId = $null
    if (-not $useGhcr) {
        Write-Step "Creating a user-assigned managed identity '$IdentityName' and granting it AcrPull..."
        $identityId = Invoke-Az -Capture -Arguments @('identity', 'create', '--name', $IdentityName, '--resource-group', $ResourceGroup, '--query', 'id', '-o', 'tsv')
        $identityPrincipalId = Invoke-Az -Capture -Arguments @('identity', 'show', '--ids', $identityId, '--query', 'principalId', '-o', 'tsv')
        $acrId = Invoke-Az -Capture -Arguments @('acr', 'show', '--name', $AcrName, '--query', 'id', '-o', 'tsv')
        Invoke-Az -Arguments @('role', 'assignment', 'create', '--assignee-object-id', $identityPrincipalId, '--assignee-principal-type', 'ServicePrincipal', '--role', 'AcrPull', '--scope', $acrId)
        Write-Info 'Waiting ~30s for the role assignment to propagate before the first pull...'
        Start-Sleep -Seconds 30
    }

    #----------------------------------------------------------------------------------
    # 13. Cosmos DB for MongoDB vCore Free Tier
    #----------------------------------------------------------------------------------
    Write-Step "Provisioning the Cosmos DB for MongoDB vCore Free Tier cluster '$ClusterName'..."
    # Newer az versions renamed the credential flags; detect which this CLI understands.
    $mongoHelp = (& az cosmosdb mongocluster create --help 2>&1 | Out-String)
    $useNewCredFlags = ($mongoHelp -match '--administrator-login\b')
    $createArgs = @(
        'cosmosdb', 'mongocluster', 'create',
        '--resource-group', $ResourceGroup,
        '--cluster-name', $ClusterName,
        '--location', $Location,
        '--server-version', '5.0',
        '--shard-node-tier', 'Free',
        '--shard-node-ha', 'false',
        '--shard-node-disk-size-gb', '32',
        '--shard-node-count', '1'
    )
    if ($useNewCredFlags) {
        $createArgs += @('--administrator-login', $CosmosAdminUser, '--administrator-login-password', $CosmosAdminPwd)
    } else {
        $createArgs += @('--administrator-user-name', $CosmosAdminUser, '--administrator-password', $CosmosAdminPwd)
    }
    Invoke-Az -Sensitive -Arguments $createArgs

    Write-Step 'Allowing Azure services to reach the cluster...'
    Invoke-Az -Arguments @('cosmosdb', 'mongocluster', 'firewall-rule', 'create', '--resource-group', $ResourceGroup, '--cluster-name', $ClusterName, '--rule-name', 'azure-services', '--start-ip-address', '0.0.0.0', '--end-ip-address', '0.0.0.0')

    Write-Step 'Building the Mongo connection string...'
    $mongoCs = Invoke-Az -Capture -Arguments @('cosmosdb', 'mongocluster', 'list-connection-string', '--resource-group', $ResourceGroup, '--cluster-name', $ClusterName, '--query', 'connectionStrings[0].connectionString', '-o', 'tsv')
    $mongoCs = $mongoCs.Replace('<user>', $CosmosAdminUser).Replace('<password>', $CosmosAdminPwd)
    if ($mongoCs -notmatch "/$([regex]::Escape($DbName))(\?|$)") {
        # Insert the database name into the path, preserving any query string.
        if ($mongoCs -match '\?') {
            $mongoCs = $mongoCs -replace '/?\?', "/$DbName`?"
        } else {
            $mongoCs = $mongoCs.TrimEnd('/') + "/$DbName"
        }
    }

    #----------------------------------------------------------------------------------
    # 14. Create the container app
    #----------------------------------------------------------------------------------
    Write-Step "Creating the container app '$AppName' (scale-to-zero, secrets wired in)..."
    $appArgs = @(
        'containerapp', 'create',
        '--name', $AppName,
        '--resource-group', $ResourceGroup,
        '--environment', $EnvName,
        '--image', $FullImage,
        '--target-port', '8080',
        '--ingress', 'external',
        '--min-replicas', '0',
        '--max-replicas', '1',
        '--cpu', '0.5', '--memory', '1Gi'
    )
    if ($useGhcr) {
        $appArgs += @('--registry-server', 'ghcr.io', '--registry-username', $GitHubUser, '--registry-password', $GitHubToken)
    } else {
        $appArgs += @('--user-assigned', $identityId, '--registry-server', "$AcrName.azurecr.io", '--registry-identity', $identityId)
    }
    $appArgs += @(
        '--secrets',
            "mongo-cs=$mongoCs",
            "api-key-pepper=$Pepper",
            "bootstrap-admin-key=$BootstrapAdminKey",
        '--env-vars',
            'ConnectionStrings__ingest=secretref:mongo-cs',
            'ApiKey__Pepper=secretref:api-key-pepper',
            'ApiKey__BootstrapAdminKey=secretref:bootstrap-admin-key',
            'ApiKey__BootstrapAdminName=admin',
            'Ingest__EnableSwagger=false',
            'Ingest__CorsDevOrigins=[]',
            'ASPNETCORE_FORWARDEDHEADERS_ENABLED=true'
    )
    Invoke-Az -Sensitive -Arguments $appArgs

    Write-Step 'Wiring liveness/readiness probes...'
    try {
        Invoke-Az -Arguments @('containerapp', 'update', '--name', $AppName, '--resource-group', $ResourceGroup, '--health-probe-method', 'get', '--liveness-probe-path', '/alive', '--liveness-probe-port', '8080', '--readiness-probe-path', '/health', '--readiness-probe-port', '8080')
    } catch {
        Write-Warn2 "Could not set health probes automatically (older az?). Configure them later in the portal. Continuing."
    }

    #----------------------------------------------------------------------------------
    # 15. Smoke test & final output
    #----------------------------------------------------------------------------------
    Write-Section 'Step 15 - Finishing up'

    $fqdn = Invoke-Az -Capture -Arguments @('containerapp', 'show', '--name', $AppName, '--resource-group', $ResourceGroup, '--query', 'properties.configuration.ingress.fqdn', '-o', 'tsv')
    $appUrl = "https://$fqdn"

    Write-Step 'Smoke-testing the health endpoint (first hit may cold-start the app)...'
    $healthy = $false
    for ($i = 1; $i -le 10; $i++) {
        try {
            $resp = Invoke-WebRequest -Uri "$appUrl/health" -UseBasicParsing -TimeoutSec 20
            if ($resp.StatusCode -eq 200) { $healthy = $true; break }
        } catch {
            Write-Info "Attempt $i/10: not ready yet, waiting..."
            Start-Sleep -Seconds 6
        }
    }
    if ($healthy) { Write-Info 'Health check passed (Healthy).' }
    else { Write-Warn2 'Health check did not pass yet. The app may still be cold-starting - try the URL in a minute.' }

    Write-Section 'Done - your Ingest deployment is live'
    Write-Host ''
    Write-Host "   URL (open this in your browser):" -ForegroundColor Green
    Write-Host "       $appUrl/" -ForegroundColor White
    Write-Host ''
    Write-Host "   Sign in with this admin API key (store it in your password manager):" -ForegroundColor Green
    Write-Host "       $BootstrapAdminKey" -ForegroundColor White
    Write-Host ''
    Write-Host "   Verify from the CLI:" -ForegroundColor Gray
    Write-Host "       curl `"$appUrl/api/me`" -H `"X-Api-Key: $BootstrapAdminKey`"" -ForegroundColor Gray
    Write-Host ''
    Write-Host "   Next steps:" -ForegroundColor Gray
    Write-Host "     * Rotate the bootstrap key: Accounts -> admin -> Manage keys -> Generate key, then revoke the bootstrap one." -ForegroundColor Gray
    Write-Host "     * First request after idle is slow (scale-to-zero cold start). Set --min-replicas 1 for snappier responses (uses the grant faster)." -ForegroundColor Gray
    if (-not $useGhcr) {
        Write-Host "     * ACR Basic keeps billing while it exists; the teardown command below removes it." -ForegroundColor Gray
    }
    Write-Host ''
    Write-Host "   Tear everything down again with:" -ForegroundColor Gray
    Write-Host "       az group delete --name $ResourceGroup --yes" -ForegroundColor Gray
    Write-Host ''
}
catch {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ''
    Write-Host "Deployment did not complete. Any resources already created are still in resource group '$ResourceGroup'." -ForegroundColor Yellow
    Write-Host "Inspect them in the Azure portal, or remove them with:  az group delete --name $ResourceGroup --yes" -ForegroundColor Yellow
    exit 1
}

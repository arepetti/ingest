#requires -Version 5.1
<#
.SYNOPSIS
    Pull a daily waste-collection summary from a vendor REST API and push it to Ingest.

.DESCRIPTION
    A MINIMAL example showing the "vendor API" integration style: GET a structured
    daily summary from a waste-management platform's REST endpoint, map its fields
    to the `garbage_collection` schema, and POST one daily submission to Ingest.

    For a self-contained run, the default source is a local static file served by
    `python -m http.server` (see README). Swap -SourceUrl for the real vendor
    endpoint in production.

.PARAMETER SourceUrl
    The vendor API URL returning the daily summary JSON.
    Defaults to http://localhost:8000/sample_response.json

.PARAMETER DryRun
    Build and print the payload but do not call the Ingest API.

.EXAMPLE
    $env:INGEST_BASE_URL = "https://ingest.example.org"
    $env:INGEST_API_KEY  = "abc12345.your-secret-here"
    ./Push-WasteRounds.ps1
#>
[CmdletBinding()]
param(
    [string]$SourceUrl = "http://localhost:8000/sample_response.json",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$SchemaName = "garbage_collection"

function New-Sample {
    param(
        [Parameter(Mandatory)][string]$ValueName,
        [Parameter(Mandatory)]$Value,
        [string]$Timestamp,
        $Note = $null
    )
    [ordered]@{
        schemaName = $SchemaName
        valueName  = $ValueName
        value      = $Value
        timestamp  = $Timestamp
        note       = $Note
    }
}

# 1. Pull the daily summary from the vendor API.
$summary = Invoke-RestMethod -Uri $SourceUrl -Method Get

# Daily cadence: one sample per day bucket, end-of-shift UTC timestamp.
$timestamp = "$($summary.operationalDate)T17:00:00Z"
$s = $summary.summary

# 2. Map vendor fields -> schema values.
$samples = [System.Collections.Generic.List[object]]::new()
$samples.Add((New-Sample -ValueName "tonnes_collected"          -Value ([double]$s.totalTonnage)                -Timestamp $timestamp))
$samples.Add((New-Sample -ValueName "routes_completed"          -Value ([int]$s.roundsCompleted)                -Timestamp $timestamp))
$samples.Add((New-Sample -ValueName "routes_missed"             -Value ([int]$s.roundsMissed)                   -Timestamp $timestamp))
$samples.Add((New-Sample -ValueName "recycling_tonnes_collected" -Value ([double]$s.recyclingTonnage)           -Timestamp $timestamp))

$breakdowns = @($summary.fleetIncidents | Where-Object { $_.type -eq "breakdown" })
$samples.Add((New-Sample -ValueName "vehicle_breakdowns"        -Value ([int]$breakdowns.Count)                 -Timestamp $timestamp))

# Conditional fields follow the schema's visibleIf rules: only send when relevant.
if ([int]$s.roundsMissed -gt 0) {
    $reason = (($summary.missedRounds | ForEach-Object { "$($_.round): $($_.reason)" }) -join "; ")
    $samples.Add((New-Sample -ValueName "routes_missed_reason" -Value $reason -Timestamp $timestamp))
}

if ($breakdowns.Count -gt 0) {
    $desc = (($breakdowns | ForEach-Object { "$($_.vehicle) ($($_.round)): $($_.description)" }) -join "; ")
    $samples.Add((New-Sample -ValueName "breakdown_description" -Value $desc -Timestamp $timestamp))
}

if ([double]$s.recyclingTonnage -gt 0) {
    $samples.Add((New-Sample -ValueName "contamination_pct" -Value ([double]$s.recyclingContaminationPercent) -Timestamp $timestamp))
}

$body = @{ samples = $samples }

if ($DryRun) {
    $body | ConvertTo-Json -Depth 6
    return
}

# 3. POST the submission to Ingest.
$baseUrl = $env:INGEST_BASE_URL
$apiKey  = $env:INGEST_API_KEY
if (-not $baseUrl -or -not $apiKey) {
    throw "Set INGEST_BASE_URL and INGEST_API_KEY (or use -DryRun)."
}

$headers = @{ "X-Api-Key" = $apiKey; "Content-Type" = "application/json" }
$json = $body | ConvertTo-Json -Depth 6

try {
    $response = Invoke-RestMethod -Uri ("{0}/api/submissions" -f $baseUrl.TrimEnd("/")) `
        -Method Post -Headers $headers -Body $json
    Write-Host "Created submission $($response.id)"
    foreach ($warning in $response.warnings) {
        Write-Host "  warning: $warning"
    }
}
catch {
    $resp = $_.Exception.Response
    if ($resp) {
        $reader = [System.IO.StreamReader]::new($resp.GetResponseStream())
        $detail = $reader.ReadToEnd()
        Write-Error "Submission failed: HTTP $([int]$resp.StatusCode)"
        try {
            $problem = $detail | ConvertFrom-Json
            if ($problem.errors) {
                foreach ($err in $problem.errors) { Write-Host "  error: $err" }
            }
            else { Write-Host "  $($problem.detail)" }
        }
        catch { Write-Host "  $detail" }
    }
    else { Write-Error $_ }
    exit 1
}

#requires -Version 5.1
<#
.SYNOPSIS
    Pull a weekly workforce summary from MHR iTrent's OData API and push it to Ingest.

.DESCRIPTION
    A MINIMAL example of the "vendor API" integration style for HR data, aimed at
    MHR iTrent. iTrent exposes OData feeds, so you query just the few columns you
    need with $select, map them to the `weekly_workforce` schema, and POST one
    weekly submission to Ingest.

    For a self-contained run, the default source is the bundled sample served by
    `python -m http.server` (see README). Swap -SourceUrl for your real iTrent
    OData endpoint in production.

.PARAMETER SourceUrl
    The iTrent OData URL returning the weekly summary JSON.
    Defaults to http://localhost:8000/sample_response.json

.PARAMETER DryRun
    Build and print the payload but do not call the Ingest API.

.EXAMPLE
    $env:INGEST_BASE_URL = "https://ingest.example.org"
    $env:INGEST_API_KEY  = "abc12345.your-secret-here"
    ./Push-Workforce.ps1
#>
[CmdletBinding()]
param(
    [string]$SourceUrl = "http://localhost:8000/sample_response.json",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$SchemaName = "weekly_workforce"

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

# 1. Query iTrent for just the columns we need. In production the URL carries the
#    OData query, e.g.
#    .../odata/v1/WeeklyWorkforceSummary?$select=activeEmployees,absenceSickness,contingentWorkers,overtimeHours&$filter=organisationUnit eq 'Waste Services' and weekEnding eq 2026-06-15
$response = Invoke-RestMethod -Uri $SourceUrl -Method Get

# OData returns matching rows under "value"; one team-week is one row.
$row = $response.value[0]
if (-not $row) { throw "iTrent returned no rows for this week - nothing to submit." }

# Weekly cadence: one sample per week bucket. Use the week-ending date (UTC).
$timestamp = "$($row.weekEnding)T00:00:00Z"

# 2. Map the iTrent columns -> schema values.
$samples = [System.Collections.Generic.List[object]]::new()
$samples.Add((New-Sample -ValueName "employees_active" -Value ([int]$row.activeEmployees)    -Timestamp $timestamp))
$samples.Add((New-Sample -ValueName "sick_leave"       -Value ([int]$row.absenceSickness)    -Timestamp $timestamp))
$samples.Add((New-Sample -ValueName "contractors"      -Value ([int]$row.contingentWorkers)  -Timestamp $timestamp))

# overtime_hours is optional: only send when there was any overtime to report.
$overtime = [double]($row.overtimeHours)
if ($overtime -gt 0) {
    $samples.Add((New-Sample -ValueName "overtime_hours" -Value $overtime -Timestamp $timestamp))
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

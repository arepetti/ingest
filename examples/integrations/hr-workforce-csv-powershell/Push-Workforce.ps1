#requires -Version 5.1
<#
.SYNOPSIS
    Push a weekly workforce snapshot to Ingest from an HR/payroll CSV export.

.DESCRIPTION
    A MINIMAL example of the "CSV export" integration style for HR data. It reads a
    per-employee weekly extract of the kind an HR/payroll system (e.g. MHR iTrent,
    Zellis ResourceLink, Civica HR, IRIS Cascade) can produce on a schedule,
    aggregates it into the `weekly_workforce` schema's values, and POSTs one weekly
    submission to Ingest.

.PARAMETER Csv
    Path to the per-employee CSV export. Defaults to the bundled sample.

.PARAMETER DryRun
    Build and print the payload but do not call the Ingest API.

.EXAMPLE
    $env:INGEST_BASE_URL = "https://ingest.example.org"
    $env:INGEST_API_KEY  = "abc12345.your-secret-here"
    ./Push-Workforce.ps1
#>
[CmdletBinding()]
param(
    [string]$Csv,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$SchemaName = "weekly_workforce"

if (-not $Csv) {
    $Csv = Join-Path $PSScriptRoot "workforce_export_2026-06-15.csv"
}

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

# 1. Read the weekly export.
$rows = Import-Csv -Path $Csv
if (-not $rows) { throw "No rows found in $Csv - nothing to submit." }

# Weekly cadence: one sample per week bucket. Use the week-ending date (UTC).
$timestamp = "$($rows[0].week_ending)T00:00:00Z"

# 2. Aggregate per-employee rows into the schema's headline figures.
$active        = @($rows | Where-Object { $_.status -eq "Active" })
$permanent     = @($active | Where-Object { $_.employment_type -eq "Permanent" })
$contractors   = @($active | Where-Object { $_.employment_type -eq "Contractor" })
$onSickLeave   = @($permanent | Where-Object { [int]$_.sick_days_this_week -gt 0 })
$overtimeTotal = ($active | Measure-Object -Property overtime_hours -Sum).Sum

# 3. Map to schema values.
$samples = [System.Collections.Generic.List[object]]::new()
$samples.Add((New-Sample -ValueName "employees_active" -Value ([int]$permanent.Count)   -Timestamp $timestamp))
$samples.Add((New-Sample -ValueName "sick_leave"       -Value ([int]$onSickLeave.Count) -Timestamp $timestamp))
$samples.Add((New-Sample -ValueName "contractors"      -Value ([int]$contractors.Count) -Timestamp $timestamp))

# overtime_hours is optional: only send when there was any overtime to report.
if ($overtimeTotal -gt 0) {
    $samples.Add((New-Sample -ValueName "overtime_hours" -Value ([double]$overtimeTotal) -Timestamp $timestamp))
}

$body = @{ samples = $samples }

if ($DryRun) {
    $body | ConvertTo-Json -Depth 6
    return
}

# 4. POST the submission.
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

<#
.SYNOPSIS
    Fails if any canonical _Measure Table measure's expression has drifted
    from the metadata-driven definition in MeasureDefinitions.json.

.DESCRIPTION
    This is a name+expression parity check against the current generated
    canonical _Measure Table representation. It deliberately does not expect
    measures to remain on Fact_Pipeline_SampleData.

.PARAMETER RepoRoot
    Path to the repository root.

.PARAMETER TmdlPath
    Optional explicit path to the canonical _Measure Table.tmdl file to check.
#>

[CmdletBinding()]
param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$TmdlPath
)

$ErrorActionPreference = 'Stop'
$CanonicalMeasureTableName = '_Measure Table'

function Normalize-Expression {
    param([string]$Expression)
    $trimmed = $Expression.Trim()
    return [System.Text.RegularExpressions.Regex]::Replace($trimmed, '\s*\r?\n\s*', ' ').Trim()
}

$measureDefinitionsPath = Join-Path $RepoRoot 'scripts\metadata\MeasureDefinitions.json'
$defaultCanonicalPath = Join-Path $RepoRoot 'BuildResult\PBIP\Pipeline_SLA_Tracker.SemanticModel\definition\tables\_Measure Table.tmdl'
if (-not $TmdlPath) { $TmdlPath = $defaultCanonicalPath }

if (-not (Test-Path $measureDefinitionsPath)) {
    Write-Error "MeasureDefinitions.json not found at: $measureDefinitionsPath"
    exit 2
}
if (-not (Test-Path $TmdlPath)) {
    Write-Error "Canonical _Measure Table.tmdl not found at: $TmdlPath"
    exit 2
}

$resolvedTmdlPath = [IO.Path]::GetFullPath((Resolve-Path $TmdlPath).Path)
if ([IO.Path]::GetFileName($resolvedTmdlPath) -ne '_Measure Table.tmdl') {
    Write-Error "Measure parity check requires the canonical '_Measure Table.tmdl'; received: $resolvedTmdlPath"
    exit 2
}

Write-Host "Checking canonical measure parity:"
Write-Host "  Source of truth : $measureDefinitionsPath"
Write-Host "  Checking against: $resolvedTmdlPath"
Write-Host ""

$definitions = Get-Content -Raw -Path $measureDefinitionsPath | ConvertFrom-Json
$canonical = @{}
foreach ($measure in $definitions.measures) {
    if ([string]::IsNullOrWhiteSpace($measure.Name)) { continue }
    if ([string]::IsNullOrWhiteSpace($measure.Expression)) {
        Write-Error "Measure '$($measure.Name)' has no Expression in MeasureDefinitions.json."
        exit 2
    }
    $canonical[$measure.Name] = Normalize-Expression $measure.Expression
}

if ($canonical.Count -eq 0) {
    Write-Error "No measures found in MeasureDefinitions.json."
    exit 2
}

$tmdlText = Get-Content -Raw -Path $resolvedTmdlPath
$measureLineRegex = [System.Text.RegularExpressions.Regex]::new(
    "(?m)^[ \t]*measure\s+(?:'(?<qname>[^']+)'|(?<name>[^=\r\n]+?))\s*=\s*(?<expr>.*)$"
)

$existing = @{}
foreach ($match in $measureLineRegex.Matches($tmdlText)) {
    $name = if ($match.Groups['qname'].Success) { $match.Groups['qname'].Value } else { $match.Groups['name'].Value.Trim() }
    $existing[$name] = Normalize-Expression $match.Groups['expr'].Value
}

$drifted = @()
$missing = @()

foreach ($name in $canonical.Keys) {
    if (-not $existing.ContainsKey($name)) {
        $missing += $name
        continue
    }
    if ($existing[$name] -ne $canonical[$name]) {
        $drifted += [pscustomobject]@{
            Measure  = $name
            Expected = $canonical[$name]
            Actual   = $existing[$name]
        }
    }
}

if ($missing.Count -eq 0 -and $drifted.Count -eq 0) {
    Write-Host "MEASURE-EXPRESSION-PARITY|PASS|Table=$CanonicalMeasureTableName|Measures=$($canonical.Count)" -ForegroundColor Green
    exit 0
}

if ($missing.Count -gt 0) {
    Write-Host "MISSING measures (defined in JSON, not present in canonical _Measure Table.tmdl):" -ForegroundColor Yellow
    $missing | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    Write-Host ""
}

if ($drifted.Count -gt 0) {
    Write-Host "DRIFTED measures (canonical TMDL expression does not match MeasureDefinitions.json):" -ForegroundColor Red
    foreach ($d in $drifted) {
        Write-Host "  - $($d.Measure)" -ForegroundColor Red
        Write-Host "      Expected: $($d.Expected)"
        Write-Host "      Actual:   $($d.Actual)"
    }
    Write-Host ""
}

Write-Error "Measure parity check FAILED: $($drifted.Count) drifted, $($missing.Count) missing."
exit 1

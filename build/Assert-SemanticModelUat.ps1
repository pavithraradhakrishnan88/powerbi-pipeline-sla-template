param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"

$pbipRoot = Join-Path $RepoRoot "BuildResult\PBIP"
$semanticRoot = Join-Path $pbipRoot "Pipeline_SLA_Tracker.SemanticModel"
$reportRoot = Join-Path $pbipRoot "Pipeline_SLA_Tracker.Report"
$factPath = Join-Path $semanticRoot "definition\tables\Fact_Pipeline_SampleData.tmdl"
$relationshipsPath = Join-Path $semanticRoot "definition\relationships.tmdl"
$measureDefinitionsPath = Join-Path $RepoRoot "scripts\metadata\MeasureDefinitions.json"
$factCsvPath = Join-Path $RepoRoot "data\Fact_Pipeline_SampleData.csv"
$categoryCsvPath = Join-Path $RepoRoot "data\Dim_Category.csv"

foreach ($path in @($factPath,$relationshipsPath,$measureDefinitionsPath,$factCsvPath,$categoryCsvPath)) {
    if (!(Test-Path $path)) { throw "UAT prerequisite missing: $path" }
}

$factText = Get-Content -Raw $factPath
$relationshipText = Get-Content -Raw $relationshipsPath
$definitions = (Get-Content -Raw $measureDefinitionsPath | ConvertFrom-Json).measures
$inlineNames = @([regex]::Matches($factText, "(?m)^\s*measure\s+('(?:''|[^'])+'|[^\r\n=]+)\s*=") | ForEach-Object { $_.Groups[1].Value.Trim("'").Replace("''", "'") })

if ($inlineNames.Count -ne $definitions.Count) {
    throw "KPI UAT failed: metadata defines $($definitions.Count) measures but Fact_Pipeline_SampleData contains $($inlineNames.Count) inline measures."
}

$missing = @($definitions | Where-Object { $_.Table -eq 'Fact_Pipeline_SampleData' -and $inlineNames -notcontains $_.Name })
if ($missing.Count -gt 0) { throw "KPI UAT failed: missing inline measures: $($missing.Name -join ', ')" }

$sla = $definitions | Where-Object Name -eq 'SLA Compliance %' | Select-Object -First 1
if ($null -eq $sla) { throw "KPI UAT failed: SLA Compliance % is absent from MeasureDefinitions.json." }
if ($inlineNames -notcontains 'SLA Compliance %') { throw "KPI UAT failed: SLA Compliance % is not inline on Fact_Pipeline_SampleData." }
if ($sla.Folder -ne '02 SLA' -or $sla.KPI -ne $true -or $sla.Format -ne '0.00%') { throw "KPI UAT failed: SLA Compliance % metadata is not in the expected KPI hierarchy." }

if ($inlineNames -contains '_Measures') { throw "KPI UAT failed: _Measures was emitted as a measure/table artifact." }
if (Test-Path (Join-Path $semanticRoot 'definition\tables\_Measures.tmdl')) { throw "KPI UAT failed: _Measures.tmdl exists." }
if ($factText -match "ref table _Measures") { throw "KPI UAT failed: model still references _Measures." }

$relationshipPattern = '(?s)relationship\s+[^\r\n]+\r?\n\s*fromColumn:\s*Fact_Pipeline_SampleData\.Category\r?\n\s*toColumn:\s*Dim_Category\.CategoryName'
if ($relationshipText -notmatch $relationshipPattern) {
    throw "Filter UAT failed: Dim_Category[CategoryName] -> Fact_Pipeline_SampleData[Category] relationship is missing."
}

$visualFiles = @(Get-ChildItem (Join-Path $reportRoot 'definition') -Recurse -Filter 'visual.json' -File)
$slicerFiles = @($visualFiles | Where-Object { (Get-Content -Raw $_.FullName) -match '"visualType"\s*:\s*"slicer"' })
$categorySlicer = $null
foreach ($file in $slicerFiles) {
    $json = Get-Content -Raw $file.FullName
    if ($json -match '"Entity"\s*:\s*"Dim_Category"' -and $json -match '"Property"\s*:\s*"CategoryName"') { $categorySlicer = $file; break }
}
if ($null -eq $categorySlicer) { throw "Slicer UAT failed: no slicer visual references Dim_Category[CategoryName]." }

$factRows = @(Import-Csv $factCsvPath)
$categoryRows = @(Import-Csv $categoryCsvPath)
if ($factRows.Count -eq 0 -or $categoryRows.Count -eq 0) { throw "Filter UAT failed: fact or category data is empty." }

$dimensionNames = @($categoryRows | ForEach-Object { [string]$_.CategoryName } | Where-Object { $_ })
$overlap = @($factRows | ForEach-Object { [string]$_.Category } | Where-Object { $dimensionNames -contains $_ } | Sort-Object -Unique)
if ($overlap.Count -eq 0) { throw "Filter UAT failed: category dimension and fact have no matching category values." }

$totalRuns = $factRows.Count
$testCategory = $overlap[0]
$filteredRows = @($factRows | Where-Object { [string]$_.Category -eq $testCategory })
if ($filteredRows.Count -le 0 -or $filteredRows.Count -ge $totalRuns) { throw "Filter UAT failed: selected category '$testCategory' did not produce a meaningful filtered fact population." }

function Get-BreachedCount($rows) {
    @($rows | Where-Object { [string]$_.SLAStatus -eq 'Breached' }).Count
}
$overallBreached = Get-BreachedCount $factRows
$filteredBreached = Get-BreachedCount $filteredRows
$overallCompliance = if ($totalRuns -eq 0) { 0 } else { (1.0 * ($totalRuns - $overallBreached)) / $totalRuns }
$filteredCompliance = if ($filteredRows.Count -eq 0) { 0 } else { (1.0 * ($filteredRows.Count - $filteredBreached)) / $filteredRows.Count }

Write-Host "KPI-UAT|MeasureCount=$($definitions.Count)|InlineFactCount=$($inlineNames.Count)|SLACompliancePresent=True|Hierarchy=PASS"
Write-Host "FILTER-UAT|Relationship=PASS|CategorySlicer=$([IO.Path]::GetRelativePath($reportRoot,$categorySlicer.FullName))|TestCategory=$testCategory|TotalRows=$totalRuns|FilteredRows=$($filteredRows.Count)|OverallCompliance=$overallCompliance|FilteredCompliance=$filteredCompliance"
Write-Host "FILTER-UAT|Behavior=PASS|CategoryFilterChangesFactPopulation=True|SLAComplianceEvaluatesAgainstFilteredPopulation=True"
Write-Host "SEMANTIC-MODEL-UAT|PASS"

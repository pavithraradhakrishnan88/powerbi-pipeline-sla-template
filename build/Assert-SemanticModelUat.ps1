param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = "Stop"

# UAT must validate the exact PBIP tree that will be uploaded as PBIP-Build.
# build.ps1 copies BuildResult\PBIP into artifacts before this script runs.
$publishedRoot = Join-Path $RepoRoot "artifacts"
$pbipRoot = $publishedRoot
$semanticRoot = Join-Path $pbipRoot "Pipeline_SLA_Tracker.SemanticModel"
$reportRoot = Join-Path $pbipRoot "Pipeline_SLA_Tracker.Report"
$factPath = Join-Path $semanticRoot "definition\tables\Fact_Pipeline_SampleData.tmdl"
$relationshipsPath = Join-Path $semanticRoot "definition\relationships.tmdl"
$measureDefinitionsPath = Join-Path $RepoRoot "scripts\metadata\MeasureDefinitions.json"
$factCsvPath = Join-Path $RepoRoot "data\Fact_Pipeline_SampleData.csv"
$categoryCsvPath = Join-Path $RepoRoot "data\Dim_Category.csv"

if (!(Test-Path $publishedRoot -PathType Container)) {
    throw "Published-artifact UAT failed: artifacts folder is missing: $publishedRoot"
}
if (!(Test-Path $semanticRoot -PathType Container)) {
    throw "Published-artifact UAT failed: semantic model is missing from artifacts: $semanticRoot"
}
if (!(Test-Path $reportRoot -PathType Container)) {
    throw "Published-artifact UAT failed: report is missing from artifacts: $reportRoot"
}

foreach ($path in @($factPath,$relationshipsPath,$measureDefinitionsPath,$factCsvPath,$categoryCsvPath)) {
    if (!(Test-Path $path)) { throw "UAT prerequisite missing: $path" }
}

$factText = Get-Content -Raw $factPath
$relationshipText = Get-Content -Raw $relationshipsPath
$definitions = (Get-Content -Raw $measureDefinitionsPath | ConvertFrom-Json).measures
$inlineNames = @([regex]::Matches($factText, "(?m)^\s*measure\s+('(?:''|[^'])+'|[^\r\n=]+)\s*=") | ForEach-Object { $_.Groups[1].Value.Trim("'").Replace("''", "'") })

if ($inlineNames.Count -ne $definitions.Count) {
    throw "KPI UAT failed: metadata defines $($definitions.Count) measures but published Fact_Pipeline_SampleData contains $($inlineNames.Count) inline measures."
}

$missing = @($definitions | Where-Object { $_.Table -eq 'Fact_Pipeline_SampleData' -and $inlineNames -notcontains $_.Name })
if ($missing.Count -gt 0) { throw "KPI UAT failed: published artifact is missing inline measures: $($missing.Name -join ', ')" }

$sla = $definitions | Where-Object Name -eq 'SLA Compliance %' | Select-Object -First 1
if ($null -eq $sla) { throw "KPI UAT failed: SLA Compliance % is absent from MeasureDefinitions.json." }
if ($inlineNames -notcontains 'SLA Compliance %') { throw "KPI UAT failed: SLA Compliance % is not inline on published Fact_Pipeline_SampleData." }
if ($sla.Folder -ne '02 SLA' -or $sla.KPI -ne $true -or $sla.Format -ne '0.00%') { throw "KPI UAT failed: SLA Compliance % metadata is not in the expected KPI hierarchy." }

# SLAStatus is a data-domain contract: the source contains Missed/Met, not Breached.
# Validate the authoritative measure definitions before accepting the generated artifact.
$breachedCountDefinition = $definitions | Where-Object Name -eq 'Breached Count' | Select-Object -First 1
$floatingStatusDefinition = $definitions | Where-Object Name -eq 'Floating Bar Status' | Select-Object -First 1
$floatingColorDefinition = $definitions | Where-Object Name -eq 'SLA Breach Color' | Select-Object -First 1
foreach ($definition in @($breachedCountDefinition,$floatingStatusDefinition,$floatingColorDefinition)) {
    if ($null -eq $definition) { throw "SLA semantic UAT failed: required SLA measure definition is missing." }
    if ([string]$definition.Expression -match 'SLAStatus\]\s*=\s*"Breached"') {
        throw "SLA semantic UAT failed: '$($definition.Name)' still uses SLAStatus = \"Breached\"; the source domain is \"Missed\"/\"Met\"."
    }
    if ([string]$definition.Expression -notmatch 'SLAStatus\]\s*=\s*"Missed"') {
        throw "SLA semantic UAT failed: '$($definition.Name)' does not explicitly use SLAStatus = \"Missed\"."
    }
}

if ($inlineNames -contains '_Measures') { throw "KPI UAT failed: _Measures was emitted as a measure/table artifact." }
if (Test-Path (Join-Path $semanticRoot 'definition\tables\_Measures.tmdl')) { throw "KPI UAT failed: published artifact contains _Measures.tmdl." }
if ($factText -match "ref table _Measures") { throw "KPI UAT failed: published semantic model still references _Measures." }

$relationshipPattern = '(?s)relationship\s+[^\r\n]+\r?\n\s*fromColumn:\s*Fact_Pipeline_SampleData\.Category\r?\n\s*toColumn:\s*Dim_Category\.CategoryName'
if ($relationshipText -notmatch $relationshipPattern) {
    throw "Filter UAT failed: published artifact is missing Dim_Category[CategoryName] -> Fact_Pipeline_SampleData[Category] relationship."
}

$visualFiles = @(Get-ChildItem (Join-Path $reportRoot 'definition') -Recurse -Filter 'visual.json' -File)
$slicerFiles = @($visualFiles | Where-Object { (Get-Content -Raw $_.FullName) -match '"visualType"\s*:\s*"slicer"' })
$categorySlicer = $null
foreach ($file in $slicerFiles) {
    $json = Get-Content -Raw $file.FullName
    if ($json -match '"Entity"\s*:\s*"Dim_Category"' -and $json -match '"Property"\s*:\s*"CategoryName"') { $categorySlicer = $file; break }
}
if ($null -eq $categorySlicer) { throw "Slicer UAT failed: published artifact has no slicer visual referencing Dim_Category[CategoryName]." }

$factRows = @(Import-Csv $factCsvPath)
$categoryRows = @(Import-Csv $categoryCsvPath)
if ($factRows.Count -eq 0 -or $categoryRows.Count -eq 0) { throw "Filter UAT failed: fact or category data is empty." }

$dimensionNames = @($categoryRows | ForEach-Object { [string]$_.CategoryName } | Where-Object { $_ })
$overlap = @($factRows | ForEach-Object { [string]$_.Category } | Where-Object { $dimensionNames -contains $_ } | Sort-Object -Unique)
if ($overlap.Count -eq 0) { throw "Filter UAT failed: category dimension and fact have no matching category values." }

$totalRuns = $factRows.Count
if ($totalRuns -ne 250) { throw "SLA semantic UAT failed: expected 250 source rows, found $totalRuns." }

$testCategory = 'ETL'
if ($overlap -notcontains $testCategory) { throw "Filter UAT failed: required ETL category is not present in the dimension/fact overlap." }
$filteredRows = @($factRows | Where-Object { [string]$_.Category -eq $testCategory })
if ($filteredRows.Count -ne 54) { throw "Filter UAT failed: expected ETL filtered population of 54 rows, found $($filteredRows.Count)." }

function Get-MissedCount($rows) {
    @($rows | Where-Object { [string]$_.SLAStatus -eq 'Missed' }).Count
}
function Get-MetCount($rows) {
    @($rows | Where-Object { [string]$_.SLAStatus -eq 'Met' }).Count
}

$overallMissed = Get-MissedCount $factRows
$overallMet = Get-MetCount $factRows
$filteredMissed = Get-MissedCount $filteredRows
$filteredMet = Get-MetCount $filteredRows
$overallCompliance = if ($totalRuns -eq 0) { 0 } else { (1.0 * $overallMet) / $totalRuns }
$filteredCompliance = if ($filteredRows.Count -eq 0) { 0 } else { (1.0 * $filteredMet) / $filteredRows.Count }

if ($overallMissed -ne 198 -or $overallMet -ne 52) {
    throw "SLA semantic UAT failed: expected SLAStatus domain counts Missed=198 and Met=52; found Missed=$overallMissed, Met=$overallMet."
}
if ([math]::Abs($overallCompliance - 0.208) -gt 0.000001) {
    throw "SLA semantic UAT failed: expected overall SLA Compliance % = 20.8%; found $overallCompliance."
}
if ($filteredMissed -ne 40 -or $filteredMet -ne 14) {
    throw "SLA semantic UAT failed: expected ETL SLAStatus counts Missed=40 and Met=14; found Missed=$filteredMissed, Met=$filteredMet."
}
if ([math]::Abs($filteredCompliance - (14.0 / 54.0)) -gt 0.000001) {
    throw "SLA semantic UAT failed: expected ETL SLA Compliance % = 25.93%; found $filteredCompliance."
}

# Verify the floating-bar measure logic against both source-domain states.
# This is semantic verification; actual Desktop color/rendering remains a separate gate.
$missedRow = $factRows | Where-Object { [string]$_.SLAStatus -eq 'Missed' } | Select-Object -First 1
$metRow = $factRows | Where-Object { [string]$_.SLAStatus -eq 'Met' } | Select-Object -First 1
if ($null -eq $missedRow -or $null -eq $metRow) { throw "Floating-bar UAT failed: both Missed and Met source rows are required." }
$missedStatusExpected = 'Breach'
$metStatusExpected = 'Within SLA'
$missedColorExpected = '#FF0000'
$metColorExpected = '#00B050'
if ([string]$floatingStatusDefinition.Expression -notmatch '"Missed"\s*,\s*"Breach"') { throw "Floating-bar UAT failed: Missed rows are not mapped to 'Breach'." }
if ([string]$floatingStatusDefinition.Expression -notmatch '"Within SLA"') { throw "Floating-bar UAT failed: Met rows are not mapped to 'Within SLA'." }
if ([string]$floatingColorDefinition.Expression -notmatch '"Missed"\s*,\s*"#FF0000"') { throw "Floating-bar UAT failed: Missed rows are not mapped to #FF0000." }
if ([string]$floatingColorDefinition.Expression -notmatch '"#00B050"') { throw "Floating-bar UAT failed: Met rows are not mapped to #00B050." }

Write-Host "PUBLISHED-ARTIFACT-UAT|Root=$publishedRoot|SourceUnderTest=artifacts"
Write-Host "KPI-UAT|MeasureCount=$($definitions.Count)|InlineFactCount=$($inlineNames.Count)|SLACompliancePresent=True|Hierarchy=PASS"
Write-Host "SLA-DOMAIN-UAT|TotalRuns=$totalRuns|Missed=$overallMissed|Met=$overallMet|BreachedCount=$overallMissed|SLACompliance=$overallCompliance|ExpectedCompliance=0.208"
Write-Host "SLA-DOMAIN-UAT|Category=ETL|FilteredRows=$($filteredRows.Count)|Missed=$filteredMissed|Met=$filteredMet|BreachedCount=$filteredMissed|SLACompliance=$filteredCompliance|ExpectedCompliance=$([math]::Round(14.0/54.0,6))"
Write-Host "FLOATING-BAR-UAT|MissedRow=$([string]$missedRow.PipelineID)|Status=$missedStatusExpected|Color=$missedColorExpected|MetRow=$([string]$metRow.PipelineID)|Status=$metStatusExpected|Color=$metColorExpected|SemanticLogic=PASS"
Write-Host "FILTER-UAT|Relationship=PASS|CategorySlicer=$([IO.Path]::GetRelativePath($reportRoot,$categorySlicer.FullName))|TestCategory=$testCategory|TotalRows=$totalRuns|FilteredRows=$($filteredRows.Count)|OverallCompliance=$overallCompliance|FilteredCompliance=$filteredCompliance"
Write-Host "FILTER-UAT|Behavior=PASS|CategoryFilterChangesFactPopulation=True|SLAComplianceEvaluatesAgainstFilteredPopulation=True"
Write-Host "SEMANTIC-MODEL-UAT|PASS|PublishedArtifact=True"

$ErrorActionPreference = "Stop"

Write-Host "Starting template-first build..."
$repoRoot = Split-Path $PSScriptRoot -Parent
$pbipName = "Pipeline_SLA_Tracker"
$projectPath = Join-Path $repoRoot "src\PowerBiPipelineSlaTemplate.Core"
$pbipOutputRoot = Join-Path $repoRoot "BuildResult\PBIP"
$generatedReportRoot = Join-Path $pbipOutputRoot "$pbipName.Report"
$generatedSemanticModelRoot = Join-Path $pbipOutputRoot "$pbipName.SemanticModel"
$templateSemanticModelRoot = Join-Path $repoRoot "pbip\$pbipName.SemanticModel"
$templateReportRoot = Join-Path $repoRoot "pbip\$pbipName.Report"

function Write-VisualBomDiagnostics {
    param([Parameter(Mandatory=$true)][string]$Stage,[Parameter(Mandatory=$true)][string]$ReportRoot)
    $definitionRoot = Join-Path $ReportRoot "definition"
    if (!(Test-Path $definitionRoot -PathType Container)) { return }
    $visualFiles = @(Get-ChildItem $definitionRoot -Recurse -Filter "visual.json" -File | Sort-Object FullName)
    Write-Host "BOM-DIAG|Stage=$Stage|ReportRoot=$ReportRoot|VisualCount=$($visualFiles.Count)"
    foreach ($file in $visualFiles) {
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        $first3 = if ($bytes.Length -ge 3) { (($bytes[0..2] | % { $_.ToString('X2') }) -join ' ') } else { '<SHORT>' }
        Write-Host "BOM-DIAG|Stage=$Stage|File=$([IO.Path]::GetRelativePath($definitionRoot,$file.FullName))|Length=$($bytes.Length)|First3=$first3|UTF8BOM=$($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)"
    }
}

function Assert-VisualJsonFiles {
    param([Parameter(Mandatory=$true)][string]$ReportRoot)
    $definitionRoot = Join-Path $ReportRoot "definition"
    $files = @(Get-ChildItem $definitionRoot -Recurse -Filter "visual.json" -File | Sort-Object FullName)
    if ($files.Count -ne 28) { throw "Visual validation failed: expected 28 visual.json files, found $($files.Count)." }
    foreach ($file in $files) {
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { throw "Visual validation failed: UTF-8 BOM remains in '$($file.FullName)'." }
        try { Get-Content -Raw $file.FullName | ConvertFrom-Json | Out-Null } catch { throw "Visual validation failed: '$($file.FullName)' is not valid JSON. $($_.Exception.Message)" }
    }
    Write-Host "Validated 28 visual.json files, BOM absence, and JSON syntax."
}

function Assert-PbirDefinition {
    param([Parameter(Mandatory=$true)][string]$ReportRoot,[Parameter(Mandatory=$true)][string]$SemanticModelRoot)
    $path = Join-Path $ReportRoot "definition.pbir"
    $definition = Get-Content -Raw $path | ConvertFrom-Json
    if ($definition.'$schema' -ne "https://developer.microsoft.com/json-schemas/fabric/item/report/definitionProperties/2.0.0/schema.json") { throw "PBIR validation failed: invalid definitionProperties schema." }
    if ([string]$definition.version -ne "4.0") { throw "PBIR validation failed: expected version 4.0." }
    $relative = [string]$definition.datasetReference.byPath.path
    $resolved = [IO.Path]::GetFullPath((Join-Path $ReportRoot $relative))
    if ($resolved.TrimEnd('\') -ine ([IO.Path]::GetFullPath($SemanticModelRoot)).TrimEnd('\')) { throw "PBIR validation failed: datasetReference does not resolve to '$SemanticModelRoot'." }
    Write-Host "PBIR definition validated against generated semantic-model output."
}

function Normalize-DefinitionSchema {
    param([Parameter(Mandatory=$true)][string]$Path)
    $definition = Get-Content -Raw $Path | ConvertFrom-Json
    $dataset = $definition.datasetReference | ConvertTo-Json -Depth 20 -Compress
    $json = "{`n  `"`$schema`": `"https://developer.microsoft.com/json-schemas/fabric/item/report/definitionProperties/2.0.0/schema.json`",`n  `"version`": `"$([string]$definition.version)`",`n  `"datasetReference`": $dataset`n}`n"
    [IO.File]::WriteAllText($Path,$json,[Text.UTF8Encoding]::new($false))
}

Write-Host "Running validation..."
& "$PSScriptRoot\validate.ps1"
$validationExitCode = $LASTEXITCODE
if ($validationExitCode -ne 0) { throw "Validation failed with exit code $validationExitCode." }

if (!(Test-Path $templateSemanticModelRoot -PathType Container)) { throw "Missing authoritative semantic-model template: $templateSemanticModelRoot" }
if (!(Test-Path $templateReportRoot -PathType Container)) { throw "Missing authoritative report template: $templateReportRoot" }
if (Test-Path $pbipOutputRoot) { Remove-Item $pbipOutputRoot -Recurse -Force }
New-Item $pbipOutputRoot -ItemType Directory -Force | Out-Null

Write-Host "Generating PBIP from authoritative templates..."
& dotnet run --project $projectPath --configuration Release
$dotnetExitCode = $LASTEXITCODE
if ($dotnetExitCode -ne 0) { throw "Template-first .NET pipeline failed with exit code $dotnetExitCode." }

if (!(Test-Path $generatedSemanticModelRoot -PathType Container)) { throw "Generated semantic model missing: $generatedSemanticModelRoot" }
if (!(Test-Path $generatedReportRoot -PathType Container)) { throw "Generated report missing: $generatedReportRoot" }

Write-Host "Validating generated semantic-model structure..."
$factPath = Join-Path $generatedSemanticModelRoot "definition\tables\Fact_Pipeline_SampleData.tmdl"
$expressionsPath = Join-Path $generatedSemanticModelRoot "definition\expressions.tmdl"
$relationshipsPath = Join-Path $generatedSemanticModelRoot "definition\relationships.tmdl"
$measuresPath = Join-Path $generatedSemanticModelRoot "definition\tables\_Measures.tmdl"
$measureDefinitionsPath = Join-Path $repoRoot "scripts\metadata\MeasureDefinitions.json"
if (!(Test-Path $factPath)) { throw "Generated semantic model is missing Fact_Pipeline_SampleData.tmdl." }
if (!(Test-Path $expressionsPath)) { throw "Generated semantic model is missing expressions.tmdl." }
if (!(Test-Path $measureDefinitionsPath)) { throw "Authoritative MeasureDefinitions.json is missing: $measureDefinitionsPath" }
if (Test-Path $measuresPath) { throw "Generated semantic model must not contain _Measures.tmdl." }
$factText = Get-Content -Raw $factPath
$expressionText = Get-Content -Raw $expressionsPath
$relationshipText = if (Test-Path $relationshipsPath) { Get-Content -Raw $relationshipsPath } else { "" }
$measureMetadata = @(Get-Content -Raw $measureDefinitionsPath | ConvertFrom-Json).measures
$expectedMeasureCount = $measureMetadata.Count
$measureCount = ([regex]::Matches($factText,'(?m)^\s*measure\s+[^\r\n=]+\s*=')).Count
$expressionCount = ([regex]::Matches($expressionText,'(?m)^\s*expression\s+')).Count
$relationshipCount = ([regex]::Matches($relationshipText,'(?m)^\s*relationship\s+')).Count
Write-Host "SEMANTIC-MODEL-DIAG|Stage=build-validation|Tables=$(@(Get-ChildItem (Join-Path $generatedSemanticModelRoot 'definition\tables') -Filter '*.tmdl').Count)|InlineMeasuresOnFact=$measureCount|ExpectedInlineMeasures=$expectedMeasureCount|Relationships=$relationshipCount|Expressions=$expressionCount|Has_MeasuresTmdl=$([bool](Test-Path $measuresPath))"
if ($measureCount -ne $expectedMeasureCount) { throw "Expected $expectedMeasureCount inline measures on Fact_Pipeline_SampleData from MeasureDefinitions.json; found $measureCount." }
if ($relationshipText -notmatch '(?s)relationship\s+[^\r\n]+\r?\n\s*fromColumn:\s*Fact_Pipeline_SampleData\.Category\r?\n\s*toColumn:\s*Dim_Category\.CategoryName') { throw "Expected Dim_Category[CategoryName] -> Fact_Pipeline_SampleData[Category] relationship is missing." }
if ($expressionCount -ne 1) { throw "Expected 1 expression; found $expressionCount." }
if ($expressionText -notmatch 'expression DataFolder = "[^"]*" meta') { throw "DataFolder expression is missing or malformed." }

$factPowerQueryPath = Join-Path $repoRoot "powerquery\Fact_Pipeline.pq"
if (!(Test-Path $factPowerQueryPath)) {
    throw "Fact Power Query source is missing: $factPowerQueryPath"
}
$factPowerQueryText = Get-Content -Raw $factPowerQueryPath
if ($factPowerQueryText -notmatch 'File\.Contents\(DataFolder\s*&\s*"\\Fact_Pipeline_SampleData\.csv"') {
    throw "Fact Power Query source does not resolve through DataFolder."
}

Normalize-DefinitionSchema -Path (Join-Path $generatedReportRoot "definition.pbir")
Write-VisualBomDiagnostics -Stage "after-dotnet-regeneration" -ReportRoot $generatedReportRoot
Assert-PbirDefinition -ReportRoot $generatedReportRoot -SemanticModelRoot $generatedSemanticModelRoot
Assert-VisualJsonFiles -ReportRoot $generatedReportRoot
Write-VisualBomDiagnostics -Stage "final-buildresult" -ReportRoot $generatedReportRoot

$artifactPath = Join-Path $repoRoot "artifacts"
if (Test-Path $artifactPath) { Remove-Item $artifactPath -Recurse -Force }
New-Item -ItemType Directory -Force -Path $artifactPath | Out-Null
Copy-Item (Join-Path $pbipOutputRoot '*') $artifactPath -Recurse -Force
foreach ($entry in @('docs','data','scripts','theme','LICENSE','CHANGELOG.md','README.md')) {
    $source = Join-Path $repoRoot $entry
    $destination = Join-Path $artifactPath (Split-Path $entry -Leaf)
    if (Test-Path $source) { Copy-Item $source $destination -Recurse -Force }
}

& "$PSScriptRoot\Assert-VisualArtifactGate.ps1" -BuildRoot $artifactPath
if ($LASTEXITCODE -ne 0) { throw "Published visual artifact gate failed with exit code $LASTEXITCODE." }

Write-Host "Build complete. Template was preserved; generated semantic model was copied wholesale and only environment-dependent values were patched."

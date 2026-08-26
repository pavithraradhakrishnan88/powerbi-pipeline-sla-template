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
        $relativePath = $file.FullName.Substring($definitionRoot.Length).TrimStart('\','/')
        Write-Host "BOM-DIAG|Stage=$Stage|File=$relativePath|Length=$($bytes.Length)|First3=$first3|UTF8BOM=$($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)"
    }
}

function Assert-CanonicalMeasureTable {
    param([Parameter(Mandatory=$true)][string]$SemanticModelRoot)
    $tablesRoot = Join-Path $SemanticModelRoot "definition\tables"
    $canonicalPath = Join-Path $tablesRoot "_Measure Table.tmdl"
    $legacyPath = Join-Path $tablesRoot "_Measures.tmdl"
    if (Test-Path $legacyPath) { throw "Measure table validation failed: legacy '_Measures.tmdl' must not exist." }
    if (!(Test-Path $canonicalPath -PathType Leaf)) { throw "Measure table validation failed: canonical '_Measure Table.tmdl' is missing." }
    $canonicalText = Get-Content -Raw $canonicalPath
    if ($canonicalText -notmatch "(?m)^table '_Measure Table'\s*$") { throw "Measure table validation failed: canonical artifact has the wrong table declaration." }
    $modelPath = Join-Path $SemanticModelRoot "definition\model.tmdl"
    if (!(Test-Path $modelPath -PathType Leaf)) { throw "Measure table validation failed: generated definition/model.tmdl is missing." }
    $modelText = Get-Content -Raw $modelPath
    if ($modelText -match "(?m)^ref table _Measures\s*$") { throw "Measure table validation failed: generated model.tmdl contains legacy '_Measures'." }
    if ($modelText -notmatch "(?m)^ref table '_Measure Table'\s*$") { throw "Measure table validation failed: generated model.tmdl does not reference canonical '_Measure Table'." }
    Write-Host "Measure-table gate passed: canonical _Measure Table present; legacy _Measures rejected."
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

function Materialize-ArtifactDataPath {
    param([Parameter(Mandatory=$true)][string]$ArtifactRoot,[Parameter(Mandatory=$true)][string]$BuildDataPath)
    $placeholder = 'C:\__PBIP_ARTIFACT_ROOT__'
    $runnerPattern = '(?i)(?:[A-Z]:\\[^\r\n"]*\\_work\\|/home/runner/|/opt/hostedtoolcache/)'
    $semanticModelRoot = Join-Path $ArtifactRoot "$pbipName.SemanticModel"
    $tmdlFiles = @(Get-ChildItem $semanticModelRoot -Recurse -Filter '*.tmdl' -File)
    foreach ($file in $tmdlFiles) {
        $text = Get-Content -Raw $file.FullName
        if ($text.Contains($BuildDataPath)) {
            $updated = $text.Replace($BuildDataPath, $placeholder)
            [IO.File]::WriteAllText($file.FullName, $updated, [Text.UTF8Encoding]::new($false))
        }
    }
    foreach ($file in $tmdlFiles) {
        $text = Get-Content -Raw $file.FullName
        if ($text -match [regex]::Escape($BuildDataPath)) { throw "Artifact materialization failed: CI build data path remains in '$($file.FullName)'." }
        if ($text -match $runnerPattern) { throw "Artifact materialization failed: runner-specific path remains in '$($file.FullName)'." }
    }
    Write-Host "Artifact PBIP data paths sanitized to a portable materialization placeholder."
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
Assert-CanonicalMeasureTable -SemanticModelRoot $generatedSemanticModelRoot

Write-Host "Validating generated semantic-model structure..."
$factPath = Join-Path $generatedSemanticModelRoot "definition\tables\Fact_Pipeline_SampleData.tmdl"
$expressionsPath = Join-Path $generatedSemanticModelRoot "definition\expressions.tmdl"
$relationshipsPath = Join-Path $generatedSemanticModelRoot "definition\relationships.tmdl"
$measuresPath = Join-Path $generatedSemanticModelRoot "definition\tables\_Measure Table.tmdl"
$legacyMeasuresPath = Join-Path $generatedSemanticModelRoot "definition\tables\_Measures.tmdl"
$measureDefinitionsPath = Join-Path $repoRoot "scripts\metadata\MeasureDefinitions.json"
$dataFolderPath = [IO.Path]::GetFullPath((Join-Path $repoRoot "data"))
if (!(Test-Path $factPath)) { throw "Generated semantic model is missing Fact_Pipeline_SampleData.tmdl." }
if (!(Test-Path $measureDefinitionsPath)) { throw "Authoritative MeasureDefinitions.json is missing: $measureDefinitionsPath" }
if (Test-Path $legacyMeasuresPath) { throw "Generated semantic model must not contain _Measures.tmdl." }
if (!(Test-Path $measuresPath)) { throw "Generated semantic model is missing canonical _Measure Table.tmdl." }
if (!(Test-Path $expressionsPath)) { throw "Generated semantic model is missing expressions.tmdl or the DataFolder Power BI parameter." }
$factText = Get-Content -Raw $factPath
$relationshipText = if (Test-Path $relationshipsPath) { Get-Content -Raw $relationshipsPath } else { "" }
$measureText = Get-Content -Raw $measuresPath
$measureMetadata = @(Get-Content -Raw $measureDefinitionsPath | ConvertFrom-Json).measures
$expectedMeasureCount = $measureMetadata.Count
$measureCount = ([regex]::Matches($measureText,'(?m)^\s*measure\s+[^\r\n=]+\s*=')).Count
$relationshipCount = ([regex]::Matches($relationshipText,'(?m)^\s*relationship\s+')).Count
Write-Host "SEMANTIC-MODEL-DIAG|Stage=build-validation|Tables=$(@(Get-ChildItem (Join-Path $generatedSemanticModelRoot 'definition\tables') -Filter '*.tmdl').Count)|Measures=$measureCount|ExpectedMeasures=$expectedMeasureCount|Relationships=$relationshipCount|Expressions=$([bool](Test-Path $expressionsPath))|Has_Legacy_MeasuresTmdl=$([bool](Test-Path $legacyMeasuresPath))"
if ($measureCount -ne $expectedMeasureCount) { throw "Expected $expectedMeasureCount measures from MeasureDefinitions.json; found $measureCount in _Measure Table.tmdl." }
if ($relationshipText -notmatch '(?s)relationship\s+[^\r\n]+\r?\n\s*fromColumn:\s*Fact_Pipeline_SampleData\.Category\r?\n\s*toColumn:\s*Dim_Category\.CategoryName') { throw "Expected Dim_Category[CategoryName] -> Fact_Pipeline_SampleData[Category] relationship is missing." }
$expectedFactPath = $dataFolderPath + '\Fact_Pipeline_SampleData.csv'
if ($factText -notmatch [regex]::Escape($expectedFactPath)) { throw "Generated Fact partition does not contain the build-time absolute data path '$expectedFactPath'." }
if ($factText -match '(?i)[A-Z]:\\[^\r\n"]*\\_work\\|/home/runner/|/opt/hostedtoolcache/') { throw "Generated Fact partition contains a CI-runner-specific path pattern." }

Normalize-DefinitionSchema -Path (Join-Path $generatedReportRoot "definition.pbir")
Write-VisualBomDiagnostics -Stage "after-dotnet-regeneration" -ReportRoot $generatedReportRoot
Assert-PbirDefinition -ReportRoot $generatedReportRoot -SemanticModelRoot $generatedSemanticModelRoot
Assert-VisualJsonFiles -ReportRoot $generatedReportRoot
Write-VisualBomDiagnostics -Stage "final-buildresult" -ReportRoot $generatedReportRoot

$artifactPath = Join-Path $repoRoot "artifacts"
if (Test-Path $artifactPath) { Remove-Item $artifactPath -Recurse -Force }
New-Item -ItemType Directory -Force -Path $artifactPath | Out-Null
Get-ChildItem $pbipOutputRoot -Force | Copy-Item -Destination $artifactPath -Recurse -Force
foreach ($entry in @('docs','data','scripts','theme','LICENSE','CHANGELOG.md','README.md')) {
    $source = Join-Path $repoRoot $entry
    $destination = Join-Path $artifactPath (Split-Path $entry -Leaf)
    if (Test-Path $source) { Copy-Item $source $destination -Recurse -Force }
}

Materialize-ArtifactDataPath -ArtifactRoot $artifactPath -BuildDataPath $dataFolderPath
Copy-Item (Join-Path $PSScriptRoot 'Materialize-PbipArtifact.ps1') (Join-Path $artifactPath 'Materialize-PbipArtifact.ps1') -Force

& "$PSScriptRoot\Assert-ArtifactIntegrity.ps1" -GeneratedRoot $pbipOutputRoot -ArtifactRoot $artifactPath -BuildDataPath $dataFolderPath
if ($LASTEXITCODE -ne 0) { throw "Artifact integrity gate failed with exit code $LASTEXITCODE." }

& "$PSScriptRoot\Assert-VisualArtifactGate.ps1" -BuildRoot $artifactPath
if ($LASTEXITCODE -ne 0) { throw "Published visual artifact gate failed with exit code $LASTEXITCODE." }

Write-Host "Build complete. Main template remains untouched; CI generated PBIP uses the build-time absolute path only during validation, while the packaged Desktop artifact contains no CI-runner path and is materialized locally after extraction."
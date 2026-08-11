# =====================================
# build.ps1
#
# Responsibilities
# - Remove unsupported orphaned PBIP local date variation output
# - Run validation
# - Regenerate/update PBIP report and semantic model through the existing .NET pipeline
# - Normalize measure bindings
# - Validate source and published PBIR/PBIP definition shape
# - Publish the regenerated PBIP output
# - Validate published visual.json inventory and JSON syntax
# - Create build artifacts
# =====================================

$ErrorActionPreference = "Stop"

Write-Host "Starting build..."

$repoRoot = Split-Path $PSScriptRoot -Parent
$pbipSourceRoot = Join-Path $repoRoot "pbip"
$pbipOutputRoot = Join-Path $repoRoot "BuildResult\PBIP"
$pbipName = "Pipeline_SLA_Tracker"
$projectPath = Join-Path $repoRoot "src\PowerBiPipelineSlaTemplate.Core"

function Assert-PbirDefinition {
    param(
        [Parameter(Mandatory = $true)][string]$ReportRoot,
        [Parameter(Mandatory = $true)][string]$SemanticModelRoot
    )

    $definitionPath = Join-Path $ReportRoot "definition.pbir"
    if (!(Test-Path $definitionPath -PathType Leaf)) { throw "PBIR validation failed: missing required '$definitionPath'." }
    try { $definition = Get-Content -Raw -Path $definitionPath | ConvertFrom-Json }
    catch { throw "PBIR validation failed: '$definitionPath' is not valid JSON. $($_.Exception.Message)" }
    if ($definition.'$schema' -ne "https://developer.microsoft.com/json-schemas/fabric/item/report/definitionProperties/2.0.0/schema.json") { throw "PBIR validation failed: definition.pbir has an invalid or missing definitionProperties schema." }
    if ([string]$definition.version -ne "4.0") { throw "PBIR validation failed: expected definition.pbir version 4.0, found '$($definition.version)'." }
    $relativePath = $definition.datasetReference.byPath.path
    if ([string]::IsNullOrWhiteSpace([string]$relativePath)) { throw "PBIR validation failed: datasetReference.byPath.path is missing." }
    if ([System.IO.Path]::IsPathRooted([string]$relativePath)) { throw "PBIR validation failed: datasetReference.byPath.path must be relative, found '$relativePath'." }
    $resolvedSemanticModel = [System.IO.Path]::GetFullPath((Join-Path $ReportRoot $relativePath))
    $expectedSemanticModel = [System.IO.Path]::GetFullPath($SemanticModelRoot)
    if ($resolvedSemanticModel.TrimEnd('\') -ine $expectedSemanticModel.TrimEnd('\')) { throw "PBIR validation failed: datasetReference path '$relativePath' does not resolve to '$SemanticModelRoot'." }
    $reportDefinitionRoot = Join-Path $ReportRoot "definition"
    if (!(Test-Path $reportDefinitionRoot -PathType Container)) { throw "PBIR validation failed: missing report definition folder '$reportDefinitionRoot'." }
    $pagesJson = Join-Path $reportDefinitionRoot "pages\pages.json"
    $reportJson = Join-Path $reportDefinitionRoot "report.json"
    if (!(Test-Path $pagesJson -PathType Leaf)) { throw "PBIR validation failed: missing '$pagesJson'." }
    if (!(Test-Path $reportJson -PathType Leaf)) { throw "PBIR validation failed: missing '$reportJson'." }
    Write-Host "PBIR definition validated: $definitionPath"
}

function Get-VisualJsonRelativePaths {
    param([Parameter(Mandatory = $true)][string]$ReportRoot)
    $definitionRoot = Join-Path $ReportRoot "definition"
    if (!(Test-Path $definitionRoot -PathType Container)) { throw "Visual validation failed: missing report definition folder '$definitionRoot'." }
    return @(Get-ChildItem -Path $definitionRoot -Recurse -Filter "visual.json" -File | ForEach-Object {
        $baseUri = [System.Uri]((Resolve-Path $definitionRoot).Path.TrimEnd('\') + '\')
        $fileUri = [System.Uri]((Resolve-Path $_.FullName).Path)
        [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($fileUri).ToString())
    } | Sort-Object)
}

function Assert-VisualJsonFiles {
    param(
        [Parameter(Mandatory = $true)][string]$ReportRoot,
        [Parameter(Mandatory = $true)][string[]]$ExpectedRelativePaths
    )
    $definitionRoot = Join-Path $ReportRoot "definition"
    $actualRelativePaths = @(Get-VisualJsonRelativePaths -ReportRoot $ReportRoot)
    $missing = @($ExpectedRelativePaths | Where-Object { $_ -notin $actualRelativePaths })
    $unexpected = @($actualRelativePaths | Where-Object { $_ -notin $ExpectedRelativePaths })
    if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) { throw "Visual validation failed: published visual.json set differs from expected. Missing: [$($missing -join ', ')]; Unexpected: [$($unexpected -join ', ')]." }
    foreach ($relativePath in $actualRelativePaths) {
        $visualPath = Join-Path $definitionRoot ($relativePath -replace '/', '\')
        try { Get-Content -Raw -Path $visualPath | ConvertFrom-Json | Out-Null }
        catch { throw "Visual validation failed: '$visualPath' is not valid JSON. $($_.Exception.Message)" }
    }
    Write-Host "Validated $($actualRelativePaths.Count) visual.json files and JSON syntax under '$ReportRoot'."
}

function Normalize-PbipMeasureBindings {
    param([Parameter(Mandatory = $true)][string]$ReportRoot)
    $visualRoot = Join-Path $ReportRoot "definition\pages"
    if (!(Test-Path $visualRoot -PathType Container)) { return }
    $updated = 0
    foreach ($visualPath in Get-ChildItem -Path $visualRoot -Recurse -Filter "visual.json" -File) {
        $json = Get-Content -Raw -Path $visualPath.FullName
        $original = $json
        $json = [regex]::Replace($json, '(?<="Measure"\s*:\s*\{\s*"Expression"\s*:\s*\{\s*"SourceRef"\s*:\s*\{\s*"Entity"\s*:\s*")Fact_Pipeline_SampleData(?=")', '_Measures')
        if ($json -ne $original) {
            [System.IO.File]::WriteAllText($visualPath.FullName, $json, [System.Text.UTF8Encoding]::new($false))
            $updated++
            Write-Host "Normalized measure bindings: $($visualPath.FullName)"
        }
    }
    Write-Host "PBIP measure binding normalization complete. Visuals updated: $updated"
}

function Ensure-PbirDefinitionSchema {
    param([Parameter(Mandatory = $true)][string]$DefinitionPath)
    if (!(Test-Path $DefinitionPath -PathType Leaf)) { throw "PBIR schema normalization failed: missing '$DefinitionPath'." }
    $definition = Get-Content -Raw -Path $DefinitionPath | ConvertFrom-Json
    $schema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definitionProperties/2.0.0/schema.json"
    $datasetReferenceJson = $definition.datasetReference | ConvertTo-Json -Depth 20 -Compress
    $json = @"
{
  `"`$schema`": `"$schema`",
  `"version`": `"$([string]$definition.version)`",
  `"datasetReference`": $datasetReferenceJson
}
"@
    [System.IO.File]::WriteAllText($DefinitionPath, $json, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Normalized PBIR definitionProperties schema: $DefinitionPath"
}

$pbipSemanticModelRoots = @(
    Join-Path -Path $pbipSourceRoot -ChildPath "$pbipName.SemanticModel"
    Join-Path -Path $pbipOutputRoot -ChildPath "$pbipName.SemanticModel"
)
foreach ($semanticModelRoot in $pbipSemanticModelRoots) {
    if (!(Test-Path $semanticModelRoot)) { continue }
    $localDateTables = Get-ChildItem -Path $semanticModelRoot -Recurse -Filter "LocalDateTable_*.tmdl" -File -ErrorAction SilentlyContinue
    foreach ($localDateTable in $localDateTables) {
        Remove-Item -Path $localDateTable.FullName -Force
        Write-Host "Removed unsupported PBIP local date variation table: $($localDateTable.FullName)"
    }
}

Write-Host "Running validation..."
& "$PSScriptRoot\validate.ps1"
if (-not $?) { throw "Validation failed. Build stopped." }
Write-Host "Validation succeeded. Continuing build..."

$sourceReportRoot = Join-Path $pbipSourceRoot "$pbipName.Report"
$sourceSemanticModelRoot = Join-Path $pbipSourceRoot "$pbipName.SemanticModel"

Write-Host "Regenerating PBIP/report through the existing .NET pipeline before robocopy..."
& dotnet run --project $projectPath --configuration Release
if ($LASTEXITCODE -ne 0) { throw "PBIP/report regeneration failed with exit code $LASTEXITCODE" }
Write-Host "Existing .NET PBIP/report regeneration completed."

Write-Host "Generating metadata..."
$outputPath = Join-Path $repoRoot "metadata\metadata.json"
if (Test-Path $projectPath) {
    & dotnet run --project $projectPath --extract-metadata --output $outputPath
    if ($LASTEXITCODE -ne 0) { throw "Metadata generation failed with exit code $LASTEXITCODE" }
}

$generatedDefinitionPath = Join-Path $sourceReportRoot "definition.pbir"
Ensure-PbirDefinitionSchema -DefinitionPath $generatedDefinitionPath
Normalize-PbipMeasureBindings -ReportRoot $sourceReportRoot
Assert-PbirDefinition -ReportRoot $sourceReportRoot -SemanticModelRoot $sourceSemanticModelRoot

$expectedVisualJsonPaths = @(Get-VisualJsonRelativePaths -ReportRoot $sourceReportRoot)
if ($expectedVisualJsonPaths.Count -eq 0) { throw "Visual validation failed: regenerated source report contains no visual.json files." }
Assert-VisualJsonFiles -ReportRoot $sourceReportRoot -ExpectedRelativePaths $expectedVisualJsonPaths
Write-Host "Regenerated source report visual.json inventory captured: $($expectedVisualJsonPaths.Count) files."

$artifactPath = Join-Path $repoRoot "artifacts"
if (Test-Path $artifactPath) { Remove-Item $artifactPath -Recurse -Force }
New-Item -ItemType Directory -Path $artifactPath | Out-Null
Write-Host "Artifacts folder created."

$releaseEntries = @("pbip", "docs", "data", "scripts", "theme", "LICENSE", "CHANGELOG.md", "README.md")
foreach ($entry in $releaseEntries) {
    $sourcePath = Join-Path $repoRoot $entry
    $destinationPath = Join-Path $artifactPath (Split-Path $entry -Leaf)
    if (Test-Path $sourcePath) {
        if (Test-Path $destinationPath) { Remove-Item $destinationPath -Recurse -Force }
        Copy-Item $sourcePath -Destination $destinationPath -Recurse -Force
        Write-Host "Included release entry: $entry"
    }
}

Write-Host "Publishing PBIP artifacts to BuildResult..."
if (!(Test-Path $pbipOutputRoot)) { New-Item -ItemType Directory -Path $pbipOutputRoot | Out-Null }

$pbipSourceFile = Join-Path $pbipSourceRoot "$pbipName.pbip"
$pbipSourceReport = Join-Path $pbipSourceRoot "$pbipName.Report"
$pbipSourceSemanticModel = Join-Path $pbipSourceRoot "$pbipName.SemanticModel"
if (!(Test-Path $pbipSourceFile -PathType Leaf)) { throw "Missing PBIP file: $pbipSourceFile" }
if (!(Test-Path $pbipSourceReport -PathType Container)) { throw "Missing report folder: $pbipSourceReport" }
if (!(Test-Path $pbipSourceSemanticModel -PathType Container)) { throw "Missing semantic model folder: $pbipSourceSemanticModel" }

cmd /c copy /Y "$pbipSourceFile" "$pbipOutputRoot\" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "PBIP file copy failed with exit code $LASTEXITCODE" }

Write-Host "Publishing regenerated report with robocopy..."
$publishedReport = Join-Path $pbipOutputRoot "$pbipName.Report"
robocopy $pbipSourceReport $publishedReport /MIR /IS /NFL /NDL /NJH /NJS /NC /NS | Out-Null
$robocopyExitCode = $LASTEXITCODE
if ($robocopyExitCode -gt 7) { throw "robocopy failed for report folder with exit code $robocopyExitCode" }
Write-Host "Report copy completed with robocopy exit code $robocopyExitCode (0-7 is success)."

Assert-VisualJsonFiles -ReportRoot $publishedReport -ExpectedRelativePaths $expectedVisualJsonPaths
Write-Host "Published report visual.json inventory matches regenerated source."

robocopy $pbipSourceSemanticModel (Join-Path $pbipOutputRoot "$pbipName.SemanticModel") /MIR /NFL /NDL /NJH /NJS /NC /NS | Out-Null
$robocopyExitCode = $LASTEXITCODE
if ($robocopyExitCode -gt 7) { throw "robocopy failed for semantic model folder with exit code $robocopyExitCode" }
Write-Host "Semantic model copy completed with robocopy exit code $robocopyExitCode (0-7 is success)."

$publishedSemanticModel = Join-Path $pbipOutputRoot "$pbipName.SemanticModel"
$publishedLocalDateTables = Get-ChildItem -Path $publishedSemanticModel -Recurse -Filter "LocalDateTable_*.tmdl" -File -ErrorAction SilentlyContinue
foreach ($localDateTable in $publishedLocalDateTables) {
    Remove-Item -Path $localDateTable.FullName -Force
    Write-Host "Removed stale published PBIP local date variation table: $($localDateTable.FullName)"
}

Assert-PbirDefinition -ReportRoot $publishedReport -SemanticModelRoot $publishedSemanticModel
Assert-VisualJsonFiles -ReportRoot $publishedReport -ExpectedRelativePaths $expectedVisualJsonPaths

$global:LASTEXITCODE = 0
Write-Host "Build complete. Regenerated PBIP output and published visual.json inventory are structurally validated."
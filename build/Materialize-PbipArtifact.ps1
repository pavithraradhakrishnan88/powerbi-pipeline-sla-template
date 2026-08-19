[CmdletBinding()]
param(
    [string]$ArtifactRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = if (![string]::IsNullOrWhiteSpace($PSScriptRoot)) { $PSScriptRoot } else { (Get-Location).Path }
}
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$SemanticModelRoot = Join-Path $ArtifactRoot "Pipeline_SLA_Tracker.SemanticModel"
$DataRoot = Join-Path $ArtifactRoot "data"
$PlaceholderRoot = "C:\__PBIP_ARTIFACT_ROOT__"
$utf8NoBom = [Text.UTF8Encoding]::new($false)

$RunnerPathPatterns = @(
    '(?i)[A-Z]:\\[^\r\n"]*\\_work\\',
    '(?i)[A-Z]:\\a\\[^\r\n"]*',
    '(?i)[A-Z]:\\actions\\[^\r\n"]*',
    '(?i)/home/runner/',
    '(?i)/opt/hostedtoolcache/',
    '(?i)/runner/_work/'
)

if (!(Test-Path $SemanticModelRoot -PathType Container)) { throw "PBIP semantic-model root was not found: $SemanticModelRoot." }
if (!(Test-Path $DataRoot -PathType Container)) { throw "Artifact data directory was not found: $DataRoot." }

$expectedFactPath = [IO.Path]::GetFullPath((Join-Path $DataRoot "Fact_Pipeline_SampleData.csv"))
$expectedCategoryPath = [IO.Path]::GetFullPath((Join-Path $DataRoot "Dim_Category.csv"))
$factPath = Join-Path $SemanticModelRoot "definition\tables\Fact_Pipeline_SampleData.tmdl"
$categoryPath = Join-Path $SemanticModelRoot "definition\tables\Dim_Category.tmdl"

foreach ($source in @($expectedFactPath,$expectedCategoryPath)) {
    if (!(Test-Path $source -PathType Leaf)) { throw "Artifact source file was not found: $source" }
}
foreach ($table in @($factPath,$categoryPath)) {
    if (!(Test-Path $table -PathType Leaf)) { throw "Materialized table TMDL was not found: $table" }
}

$tmdlFiles = @(Get-ChildItem $SemanticModelRoot -Recurse -Filter "*.tmdl" -File)
$replacementCount = 0
$factReplacementCount = 0
$categoryReplacementCount = 0

# Materialization boundary: resolve the shared DataFolder expression first.
# Example:
#   File.Contents(DataFolder & "\Dim_Category.csv")
# becomes:
#   File.Contents("<artifact>\data\Dim_Category.csv")
# This happens before any Desktop/UAT/visual validation step.
$dataFolderFileContentsPattern = '(?i)File\.Contents\(\s*DataFolder\s*&\s*"[\\/]([^"\r\n]+)"\s*\)'
$plainFileContentsPattern = '(?i)File\.Contents\(\s*"(?:[^"\r\n]*[\\/])?([^"\\/]+\.csv)"\s*\)'

foreach ($file in $tmdlFiles) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $updated = $text

    if ($updated.Contains($PlaceholderRoot)) {
        $updated = $updated.Replace($PlaceholderRoot, $DataRoot.TrimEnd('\'))
    }

    # Step 1: resolve shared DataFolder references to concrete artifact-local paths.
    $updated = [regex]::Replace($updated, $dataFolderFileContentsPattern,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            $fileName = Split-Path $match.Groups[1].Value -Leaf
            $sourcePath = [IO.Path]::GetFullPath((Join-Path $DataRoot $fileName))
            if (!(Test-Path $sourcePath -PathType Leaf)) { throw "Materialization failed: DataFolder references '$fileName' but '$sourcePath' does not exist." }
            if ($fileName -ieq 'Fact_Pipeline_SampleData.csv') { $script:factReplacementCount++ }
            if ($fileName -ieq 'Dim_Category.csv') { $script:categoryReplacementCount++ }
            $script:replacementCount++
            return 'File.Contents("' + $sourcePath + '")'
        })

    # Step 1b: normalize any remaining direct File.Contents CSV path to artifact-local data.
    $updated = [regex]::Replace($updated, $plainFileContentsPattern,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            $fileName = $match.Groups[1].Value
            $sourcePath = [IO.Path]::GetFullPath((Join-Path $DataRoot $fileName))
            if (!(Test-Path $sourcePath -PathType Leaf)) { throw "Materialization failed: File.Contents references '$fileName' but '$sourcePath' does not exist." }
            if ($fileName -ieq 'Fact_Pipeline_SampleData.csv') { $script:factReplacementCount++ }
            if ($fileName -ieq 'Dim_Category.csv') { $script:categoryReplacementCount++ }
            $script:replacementCount++
            return 'File.Contents("' + $sourcePath + '")'
        })

    if ($updated -ne $text) { $replacementCount++ }
    [IO.File]::WriteAllText($file.FullName, $updated, $utf8NoBom)
}

# Materialization gate: no unresolved shared expression, placeholder, or runner path.
foreach ($file in $tmdlFiles) {
    $text = [IO.File]::ReadAllText($file.FullName)
    if ($text -match $dataFolderFileContentsPattern) { throw "Artifact materialization failed: unresolved DataFolder File.Contents expression remains in '$($file.FullName)'." }
    if ($text -match [regex]::Escape($PlaceholderRoot)) { throw "Artifact materialization failed: placeholder remains in '$($file.FullName)'." }
    foreach ($runnerPattern in $RunnerPathPatterns) {
        if ($text -match $runnerPattern) { throw "Artifact materialization failed: runner-specific path remains in '$($file.FullName)'. Pattern: $runnerPattern" }
    }
}

$bomFiles = @()
foreach ($file in $tmdlFiles) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $bomFiles += $file.FullName }
}
if ($bomFiles.Count -gt 0) { throw "Artifact materialization failed: $($bomFiles.Count) TMDL file(s) contain UTF-8 BOM." }

$factText = [IO.File]::ReadAllText($factPath)
$categoryText = [IO.File]::ReadAllText($categoryPath)
if ($factText -notmatch [regex]::Escape($expectedFactPath)) { throw "Materialized Fact partition does not contain '$expectedFactPath'." }
if ($categoryText -notmatch [regex]::Escape($expectedCategoryPath)) { throw "Materialized Dim_Category partition does not contain '$expectedCategoryPath'." }

Write-Host "DATA-MATERIALIZATION-GATE|PASS|Fact=PASS|Dim_Category=PASS|DataFolderResolved=PASS" -ForegroundColor Green
Write-Host "ARTIFACT-FACT-PATH-GATE|PASS" -ForegroundColor Green
Write-Host "TMDL-UTF8-NOBOM-GATE|PASS" -ForegroundColor Green
Write-Host "ArtifactRoot=$ArtifactRoot"
Write-Host "DataRoot=$DataRoot"
Write-Host "FactSource=$expectedFactPath"
Write-Host "Dim_CategorySource=$expectedCategoryPath"
Write-Host "FactPathReplacements=$factReplacementCount"
Write-Host "Dim_CategoryPathReplacements=$categoryReplacementCount"
Write-Host "RunnerPaths=0"
Write-Host "BomFiles=0"

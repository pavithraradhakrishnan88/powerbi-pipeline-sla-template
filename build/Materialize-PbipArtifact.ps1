[CmdletBinding()]
param(
    [string]$ArtifactRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$SemanticModelRoot = Join-Path $ArtifactRoot "Pipeline_SLA_Tracker.SemanticModel"
$DataRoot = Join-Path $ArtifactRoot "data"
$PlaceholderRoot = "C:\__PBIP_ARTIFACT_ROOT__"
$RunnerPathPattern = '(?i)(?:[A-Z]:\\[^\r\n"]*\\_work\\|/home/runner/|/opt/hostedtoolcache/)'

if (!(Test-Path $SemanticModelRoot -PathType Container)) {
    throw "PBIP semantic-model root was not found: $SemanticModelRoot"
}
if (!(Test-Path $DataRoot -PathType Container)) {
    throw "Artifact data directory was not found: $DataRoot"
}

$tmdlFiles = @(Get-ChildItem $SemanticModelRoot -Recurse -Filter "*.tmdl" -File)
$replacementRoot = $DataRoot.TrimEnd('\')
$replacementCount = 0

foreach ($file in $tmdlFiles) {
    $text = Get-Content -Raw $file.FullName
    if ($text.Contains($PlaceholderRoot)) {
        $updated = $text.Replace($PlaceholderRoot, $replacementRoot)
        [IO.File]::WriteAllText($file.FullName, $updated, [Text.UTF8Encoding]::new($false))
        $replacementCount++
    }
}

foreach ($file in $tmdlFiles) {
    $text = Get-Content -Raw $file.FullName
    if ($text -match [regex]::Escape($PlaceholderRoot)) {
        throw "Artifact materialization failed: placeholder remains in '$($file.FullName)'."
    }
    if ($text -match $RunnerPathPattern) {
        throw "Artifact materialization failed: runner-specific path remains in '$($file.FullName)'."
    }
}

$expectedFact = Join-Path $DataRoot "Fact_Pipeline_SampleData.csv"
$factPath = Join-Path $SemanticModelRoot "definition\tables\Fact_Pipeline_SampleData.tmdl"
if (!(Test-Path $factPath)) {
    throw "Materialized Fact_Pipeline_SampleData.tmdl was not found: $factPath"
}
$factText = Get-Content -Raw $factPath
$expectedFactPath = [IO.Path]::GetFullPath($expectedFact)
if ($factText -notmatch [regex]::Escape($expectedFactPath)) {
    throw "Materialized Fact partition does not contain the artifact-local data path '$expectedFactPath'."
}
if (!(Test-Path $expectedFact -PathType Leaf)) {
    throw "Materialized Fact source file was not found: $expectedFact"
}

Write-Host "PBIP artifact materialized for Desktop."
Write-Host "Artifact root: $ArtifactRoot"
Write-Host "Data root: $DataRoot"
Write-Host "TMDL files materialized: $replacementCount"
Write-Host "Fact source: $expectedFactPath"

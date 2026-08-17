[CmdletBinding()]
param(
    [string]$BuildRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) "BuildResult\PBIP"),
    [string]$ArtifactRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts"),
    [string]$BuildDataPath = (Join-Path (Split-Path $PSScriptRoot -Parent) "data")
)

$ErrorActionPreference = "Stop"
$pbipName = "Pipeline_SLA_Tracker"
$generatedReportRoot = Join-Path $BuildRoot "$pbipName.Report"
$artifactReportRoot = Join-Path $ArtifactRoot "$pbipName.Report"
$generatedSemanticRoot = Join-Path $BuildRoot "$pbipName.SemanticModel"
$artifactSemanticRoot = Join-Path $ArtifactRoot "$pbipName.SemanticModel"
$placeholder = 'C:\__PBIP_ARTIFACT_ROOT__'
$runnerPattern = '(?i)(?:[A-Z]:\\[^\r\n"]*\\_work\\|/home/runner/|/opt/hostedtoolcache/)'
$BuildDataPath = [IO.Path]::GetFullPath($BuildDataPath).TrimEnd('\')

foreach ($path in @($generatedReportRoot,$artifactReportRoot,$generatedSemanticRoot,$artifactSemanticRoot)) {
    if (!(Test-Path $path -PathType Container)) { throw "Artifact integrity gate: missing required directory '$path'." }
}

function Get-RelativeFiles([string]$Root) {
    $rootFull = [IO.Path]::GetFullPath($Root)
    @(Get-ChildItem $rootFull -Recurse -File | ForEach-Object {
        [PSCustomObject]@{ Relative = [IO.Path]::GetRelativePath($rootFull, $_.FullName); FullName = $_.FullName }
    } | Sort-Object Relative)
}

function Get-BytesHash([string]$Path) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
}

$generatedReportFiles = @(Get-RelativeFiles $generatedReportRoot)
$artifactReportFiles = @(Get-RelativeFiles $artifactReportRoot)
if ((@($generatedReportFiles.Relative) -join "`n") -ne (@($artifactReportFiles.Relative) -join "`n")) {
    throw "Artifact integrity gate: report file set differs between generated PBIP and packaged artifact."
}
foreach ($file in $generatedReportFiles) {
    $artifactFile = Join-Path $artifactReportRoot $file.Relative
    $generatedHash = Get-BytesHash $file.FullName
    $artifactHash = Get-BytesHash $artifactFile
    if ($generatedHash -ne $artifactHash) {
        throw "Artifact integrity gate: report file changed after validation: '$($file.Relative)'."
    }
}

$generatedSemanticFiles = @(Get-RelativeFiles $generatedSemanticRoot)
$artifactSemanticFiles = @(Get-RelativeFiles $artifactSemanticRoot)
if ((@($generatedSemanticFiles.Relative) -join "`n") -ne (@($artifactSemanticFiles.Relative) -join "`n")) {
    throw "Artifact integrity gate: semantic-model file set differs between generated PBIP and packaged artifact."
}

$allowedChangedFiles = @()
foreach ($file in $generatedSemanticFiles) {
    $artifactFile = Join-Path $artifactSemanticRoot $file.Relative
    $generatedText = $null
    $artifactText = $null
    $isText = $file.Relative -like '*.tmdl'
    if ($isText) {
        $generatedText = Get-Content -Raw $file.FullName
        $artifactText = Get-Content -Raw $artifactFile
        $expectedArtifactText = $generatedText.Replace($BuildDataPath, $placeholder)
        if ($artifactText -ne $expectedArtifactText) {
            if ((Get-BytesHash $file.FullName) -ne (Get-BytesHash $artifactFile)) {
                throw "Artifact integrity gate: semantic-model content changed beyond the allowed data-path transformation: '$($file.Relative)'."
            }
        } elseif ($generatedText -ne $artifactText) {
            $allowedChangedFiles += $file.Relative
        }
    } else {
        if ((Get-BytesHash $file.FullName) -ne (Get-BytesHash $artifactFile)) {
            throw "Artifact integrity gate: non-TMDL semantic-model file changed after validation: '$($file.Relative)'."
        }
    }
}

foreach ($file in $artifactSemanticFiles) {
    $text = if ($file.Relative -like '*.tmdl') { Get-Content -Raw $file.FullName } else { "" }
    if ($text -match $runnerPattern) {
        throw "Artifact integrity gate: runner-specific path remains in packaged semantic model: '$($file.Relative)'."
    }
}

Write-Host "ARTIFACT-INTEGRITY-GATE|PASS|GeneratedReport=ByteIdentical|ReportChanged=0|SemanticModelChanges=DataPathOnly|ChangedTmdlFiles=$($allowedChangedFiles.Count)|RunnerPaths=0"
if ($allowedChangedFiles.Count -gt 0) {
    foreach ($path in $allowedChangedFiles) { Write-Host "ARTIFACT-INTEGRITY-GATE|AllowedChange=$path" }
}

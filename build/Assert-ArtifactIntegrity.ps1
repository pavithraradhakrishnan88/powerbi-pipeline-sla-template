[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GeneratedRoot,
    [Parameter(Mandatory=$true)][string]$ArtifactRoot,
    [Parameter(Mandatory=$true)][string]$BuildDataPath
)

$ErrorActionPreference = 'Stop'
$Placeholder = 'C:\__PBIP_ARTIFACT_ROOT__'
$RunnerPathPattern = '(?i)(?:[A-Z]:\\[^\r\n"]*\\_work\\|/home/runner/|/opt/hostedtoolcache/)'

$GeneratedRoot = [IO.Path]::GetFullPath($GeneratedRoot)
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$BuildDataPath = [IO.Path]::GetFullPath($BuildDataPath).TrimEnd('\')

function Get-RelativeFiles {
    param([Parameter(Mandatory=$true)][string]$Root)
    if (!(Test-Path $Root -PathType Container)) { throw "Missing directory: $Root" }
    $files = @(Get-ChildItem $Root -Recurse -File | ForEach-Object {
        [PSCustomObject]@{
            Relative = [IO.Path]::GetRelativePath($Root, $_.FullName)
            FullName = $_.FullName
        }
    } | Sort-Object Relative)
    return $files
}

function Get-DirectoryHash {
    param([Parameter(Mandatory=$true)][string]$Root)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $builder = [Text.StringBuilder]::new()
        foreach ($file in Get-RelativeFiles -Root $Root) {
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            $hash = ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join ''
            [void]$builder.Append($file.Relative.Replace('\','/'))
            [void]$builder.Append("|")
            [void]$builder.Append($hash)
            [void]$builder.Append("`n")
        }
        $manifestBytes = [Text.Encoding]::UTF8.GetBytes($builder.ToString())
        return (($sha.ComputeHash($manifestBytes) | ForEach-Object { $_.ToString('x2') }) -join '')
    }
    finally { $sha.Dispose() }
}

function Assert-SameFileSet {
    param([string]$GeneratedDir,[string]$ArtifactDir,[string]$Label)
    $generated = @(Get-RelativeFiles -Root $GeneratedDir | Select-Object -ExpandProperty Relative)
    $artifact = @(Get-RelativeFiles -Root $ArtifactDir | Select-Object -ExpandProperty Relative)
    $diff = Compare-Object -ReferenceObject $generated -DifferenceObject $artifact
    if ($null -ne $diff) {
        $details = ($diff | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join '; '
        throw "$Label file-set mismatch: $details"
    }
}

$generatedReport = Join-Path $GeneratedRoot 'Pipeline_SLA_Tracker.Report'
$artifactReport = Join-Path $ArtifactRoot 'Pipeline_SLA_Tracker.Report'
$generatedModel = Join-Path $GeneratedRoot 'Pipeline_SLA_Tracker.SemanticModel'
$artifactModel = Join-Path $ArtifactRoot 'Pipeline_SLA_Tracker.SemanticModel'

Assert-SameFileSet -GeneratedDir $generatedReport -ArtifactDir $artifactReport -Label 'Report'
Assert-SameFileSet -GeneratedDir $generatedModel -ArtifactDir $artifactModel -Label 'Semantic-model'

$reportGeneratedHash = Get-DirectoryHash -Root $generatedReport
$reportPackagedHash = Get-DirectoryHash -Root $artifactReport
if ($reportGeneratedHash -ne $reportPackagedHash) {
    throw "Report integrity failure: generated and packaged report hashes differ. Generated=$reportGeneratedHash Packaged=$reportPackagedHash"
}

$reportChanged = 0
foreach ($file in Get-RelativeFiles -Root $generatedReport) {
    $artifactFile = Join-Path $artifactReport $file.Relative
    if (!(Test-Path $artifactFile -PathType Leaf) -or -not [Linq.Enumerable]::SequenceEqual([IO.File]::ReadAllBytes($file.FullName), [IO.File]::ReadAllBytes($artifactFile))) {
        $reportChanged++
    }
}
if ($reportChanged -ne 0) { throw "Report integrity failure: $reportChanged report files changed." }

$semanticModelNonDataChanges = 0
foreach ($file in Get-RelativeFiles -Root $generatedModel) {
    $artifactFile = Join-Path $artifactModel $file.Relative
    $generatedText = Get-Content -Raw $file.FullName
    $artifactText = Get-Content -Raw $artifactFile
    $isTmdl = [IO.Path]::GetExtension($file.FullName).Equals('.tmdl',[StringComparison]::OrdinalIgnoreCase)
    if ($isTmdl) {
        $expectedArtifactText = $generatedText.Replace($BuildDataPath, $Placeholder)
        if ($artifactText -ne $expectedArtifactText) { $semanticModelNonDataChanges++ }
    } elseif (-not [Linq.Enumerable]::SequenceEqual([IO.File]::ReadAllBytes($file.FullName), [IO.File]::ReadAllBytes($artifactFile))) {
        $semanticModelNonDataChanges++
    }
}
if ($semanticModelNonDataChanges -ne 0) { throw "Semantic-model integrity failure: $semanticModelNonDataChanges non-data changes detected." }

$runnerPaths = 0
foreach ($root in @($artifactReport,$artifactModel)) {
    foreach ($file in Get-RelativeFiles -Root $root) {
        $text = Get-Content -Raw $file.FullName
        if ($text -match $RunnerPathPattern) { $runnerPaths++ }
    }
}
if ($runnerPaths -ne 0) { throw "Artifact integrity failure: $runnerPaths files contain CI-runner-specific paths." }

Write-Host "ARTIFACT-INTEGRITY-GATE|PASS"
Write-Host "ReportGeneratedHash=$reportGeneratedHash"
Write-Host "ReportPackagedHash=$reportPackagedHash"
Write-Host "ReportFilesChanged=$reportChanged"
Write-Host "SemanticModelNonDataChanges=$semanticModelNonDataChanges"
Write-Host "DataPathTransformationsOnly=PASS"
Write-Host "RunnerPaths=$runnerPaths"

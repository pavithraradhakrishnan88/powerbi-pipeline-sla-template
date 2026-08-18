[CmdletBinding()]
param(
    [string]$ArtifactRoot = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    if (![string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $ArtifactRoot = $PSScriptRoot
    } else {
        $ArtifactRoot = (Get-Location).Path
    }
}

if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    throw "ArtifactRoot could not be determined. Run this script from the extracted artifact directory or pass -ArtifactRoot 'C:\path\to\artifact'."
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

if (!(Test-Path $SemanticModelRoot -PathType Container)) {
    throw "PBIP semantic-model root was not found: $SemanticModelRoot."
}
if (!(Test-Path $DataRoot -PathType Container)) {
    throw "Artifact data directory was not found: $DataRoot"
}

$expectedFact = Join-Path $DataRoot "Fact_Pipeline_SampleData.csv"
$expectedFactPath = [IO.Path]::GetFullPath($expectedFact)
$factPath = Join-Path $SemanticModelRoot "definition\tables\Fact_Pipeline_SampleData.tmdl"
$expressionsPath = Join-Path $SemanticModelRoot "definition\expressions.tmdl"

if (!(Test-Path $expectedFact -PathType Leaf)) {
    throw "Artifact Fact source file was not found: $expectedFact"
}
if (!(Test-Path $factPath -PathType Leaf)) {
    throw "Materialized Fact_Pipeline_SampleData.tmdl was not found: $factPath"
}

$tmdlFiles = @(Get-ChildItem $SemanticModelRoot -Recurse -Filter "*.tmdl" -File)
$replacementRoot = $DataRoot.TrimEnd('\')
$replacementCount = 0
$factReplacementCount = 0
$dataFolderReferenceCount = 0

# Resolve the shared DataFolder expression before rewriting dependent M expressions.
# The generated model may define DataFolder in expressions.tmdl and reference it as:
# File.Contents(DataFolder & "\\Dim_Category.csv")
$dataFolderValue = $null
if (Test-Path $expressionsPath -PathType Leaf) {
    $expressionsText = [IO.File]::ReadAllText($expressionsPath)
    $dataFolderMatch = [regex]::Match(
        $expressionsText,
        '(?im)^\s*expression\s+DataFolder\s*=\s*"((?:""|[^"\r\n])*)"'
    )

    if ($dataFolderMatch.Success) {
        $dataFolderValue = $dataFolderMatch.Groups[1].Value.Replace('""', '"')
    }
}

if ([string]::IsNullOrWhiteSpace($dataFolderValue)) {
    # Keep materialization deterministic even when the expression declaration is absent.
    # The artifact-local data directory is the only valid Desktop target.
    $dataFolderValue = $replacementRoot
}

# Normalize the resolved expression value. This is intentionally only used to
# identify the shared source root; all generated references are rewritten to
# artifact-local files below.
$dataFolderValue = $dataFolderValue.Replace('/', '\').TrimEnd('\')

# Materialize DataFolder-based File.Contents expressions and any existing Fact
# File.Contents path to artifact-local absolute paths. This leaves the generated
# report/model structure unchanged.
$fileContentsDataFolderPattern = '(?i)File\.Contents\(\s*DataFolder\s*&\s*"([^"]+)"\s*\)'
$factPattern = '(?i)File\.Contents\("(?:[^"\r\n]*[\\/])?Fact_Pipeline_SampleData\.csv"\)'

foreach ($file in $tmdlFiles) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $updated = $text

    if ($updated.Contains($PlaceholderRoot)) {
        $updated = $updated.Replace($PlaceholderRoot, $replacementRoot)
    }

    $updated = [regex]::Replace(
        $updated,
        $fileContentsDataFolderPattern,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            $relativeFile = $match.Groups[1].Value.Replace('/', '\').TrimStart('\')
            $targetPath = [IO.Path]::GetFullPath((Join-Path $replacementRoot $relativeFile))
            if (!(Test-Path $targetPath -PathType Leaf)) {
                throw "DataFolder materialization target was not found: $targetPath"
            }
            $script:dataFolderReferenceCount++
            return 'File.Contents("' + $targetPath + '")'
        }
    )

    $updated = [regex]::Replace(
        $updated,
        $factPattern,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            $script:factReplacementCount++
            return 'File.Contents("' + $expectedFactPath + '")'
        }
    )

    if ($updated -ne $text) {
        $replacementCount++
    }

    # Always rewrite as UTF-8 without BOM so Desktop receives deterministic TMDL.
    [IO.File]::WriteAllText($file.FullName, $updated, $utf8NoBom)
}

# Enforce placeholder, DataFolder, and runner-path gates after materialization.
foreach ($file in $tmdlFiles) {
    $text = [IO.File]::ReadAllText($file.FullName)

    if ($text -match [regex]::Escape($PlaceholderRoot)) {
        throw "Artifact materialization failed: placeholder remains in '$($file.FullName)'."
    }

    foreach ($runnerPattern in $RunnerPathPatterns) {
        if ($text -match $runnerPattern) {
            throw "Artifact materialization failed: runner-specific path remains in '$($file.FullName)'. Pattern: $runnerPattern"
        }
    }

    if ($text -match '(?i)\bDataFolder\b') {
        throw "Artifact materialization failed: unresolved DataFolder reference remains in '$($file.FullName)'."
    }
}

# Byte-level UTF-8 BOM gate.
$bomFiles = @()
foreach ($file in $tmdlFiles) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $bomFiles += $file.FullName
    }
}

if ($bomFiles.Count -gt 0) {
    $bomFiles | ForEach-Object { Write-Host "UTF-8 BOM detected: $_" -ForegroundColor Red }
    throw "Artifact materialization failed: $($bomFiles.Count) TMDL file(s) contain UTF-8 BOM."
}

$factText = [IO.File]::ReadAllText($factPath)
if ($factText -notmatch [regex]::Escape($expectedFactPath)) {
    throw "Materialized Fact partition does not contain the artifact-local data path '$expectedFactPath'."
}
if (!(Test-Path $expectedFact -PathType Leaf)) {
    throw "Materialized Fact source file was not found: $expectedFact"
}

$platformFiles = @(Get-ChildItem $ArtifactRoot -Recurse -Filter ".platform" -File)
if ($platformFiles.Count -lt 2) {
    throw "Artifact materialization failed: expected both PBIP and semantic-model .platform files; found $($platformFiles.Count)."
}

Write-Host "PBIP artifact materialized for Desktop." -ForegroundColor Green
Write-Host "Artifact root: $ArtifactRoot"
Write-Host "Data root: $DataRoot"
Write-Host "Resolved DataFolder: $dataFolderValue"
Write-Host "TMDL files materialized: $replacementCount"
Write-Host "DataFolder File.Contents replacements: $dataFolderReferenceCount"
Write-Host "Fact path replacements: $factReplacementCount"
Write-Host "Fact source: $expectedFactPath"
Write-Host "ARTIFACT-FACT-PATH-GATE|PASS" -ForegroundColor Green
Write-Host "TMDL-UTF8-NOBOM-GATE|PASS" -ForegroundColor Green
Write-Host "DataFolderReferences=0" -ForegroundColor Green
Write-Host "PlatformFiles=$($platformFiles.Count)"
Write-Host "PlatformFiles=PASS" -ForegroundColor Green
Write-Host "RunnerPaths=0"
Write-Host "BomFiles=0"
Write-Host "CsvExists=True"
Write-Host "Materialize-PbipArtifact ........ PASS" -ForegroundColor Green

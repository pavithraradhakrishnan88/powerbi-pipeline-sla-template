[CmdletBinding()]
param(
    [string]$ArtifactRoot = ""
)

$ErrorActionPreference = "Stop"

# When executed as a .ps1 file, use the directory containing the script.
# When pasted into an interactive PowerShell session, PSScriptRoot is empty,
# so use the current directory instead. An explicit -ArtifactRoot always wins.
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

# Block paths that identify GitHub-hosted runners or other CI workspaces.
# Include the known GitHub Actions Windows path form used by this project.
$RunnerPathPatterns = @(
    '(?i)[A-Z]:\\[^\r\n"]*\\_work\\',
    '(?i)[A-Z]:\\a\\[^\r\n"]*',
    '(?i)[A-Z]:\\actions\\[^\r\n"]*',
    '(?i)/home/runner/',
    '(?i)/opt/hostedtoolcache/',
    '(?i)/runner/_work/'
)

if (!(Test-Path $SemanticModelRoot -PathType Container)) {
    throw "PBIP semantic-model root was not found: $SemanticModelRoot. Make sure ArtifactRoot points to the extracted artifact directory."
}
if (!(Test-Path $DataRoot -PathType Container)) {
    throw "Artifact data directory was not found: $DataRoot. Make sure ArtifactRoot points to the extracted artifact directory."
}

$expectedFact = Join-Path $DataRoot "Fact_Pipeline_SampleData.csv"
$expectedFactPath = [IO.Path]::GetFullPath($expectedFact)
$factPath = Join-Path $SemanticModelRoot "definition\tables\Fact_Pipeline_SampleData.tmdl"

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

# Match any File.Contents(...) expression whose filename is Fact_Pipeline_SampleData.csv,
# regardless of whether the current source is a CI runner path, placeholder path, or
# another absolute/relative path. This makes Fact materialization deterministic.
$factPattern = '(?i)File\.Contents\("(?:[^"\r\n]*[\\/])?Fact_Pipeline_SampleData\.csv"\)'

foreach ($file in $tmdlFiles) {
    # ReadAllText normalizes away an existing UTF-8 BOM for the in-memory string.
    # Every write below explicitly uses UTF8Encoding(false), guaranteeing no BOM.
    $text = [IO.File]::ReadAllText($file.FullName)
    $updated = $text

    if ($updated.Contains($PlaceholderRoot)) {
        $updated = $updated.Replace($PlaceholderRoot, $replacementRoot)
    }

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
        [IO.File]::WriteAllText($file.FullName, $updated, $utf8NoBom)
        $replacementCount++
    } else {
        # Existing TMDL files may already contain a UTF-8 BOM. Rewrite them even when
        # their text content is unchanged because Power BI Desktop requires BOM-free UTF-8.
        [IO.File]::WriteAllText($file.FullName, $text, $utf8NoBom)
    }
}

# Re-read every TMDL after materialization and enforce placeholder/runner-path gates.
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
}

# Byte-level encoding gate. Power BI Desktop July 2026 rejects UTF-8 TMDL files
# containing an EF BB BF BOM, so verify the actual bytes rather than trusting the reader.
$bomFiles = @()
foreach ($file in $tmdlFiles) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if (
        $bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF
    ) {
        $bomFiles += $file.FullName
    }
}

if ($bomFiles.Count -gt 0) {
    $bomFiles | ForEach-Object { Write-Host "UTF-8 BOM detected: $_" -ForegroundColor Red }
    throw "Artifact materialization failed: $($bomFiles.Count) TMDL file(s) contain UTF-8 BOM. Power BI Desktop requires UTF-8 without BOM."
}

$factText = [IO.File]::ReadAllText($factPath)
if ($factText -notmatch [regex]::Escape($expectedFactPath)) {
    throw "Materialized Fact partition does not contain the artifact-local data path '$expectedFactPath'."
}
if (!(Test-Path $expectedFact -PathType Leaf)) {
    throw "Materialized Fact source file was not found: $expectedFact"
}

Write-Host "PBIP artifact materialized for Desktop." -ForegroundColor Green
Write-Host "Artifact root: $ArtifactRoot"
Write-Host "Data root: $DataRoot"
Write-Host "TMDL files materialized: $replacementCount"
Write-Host "Fact path replacements: $factReplacementCount"
Write-Host "Fact source: $expectedFactPath"
Write-Host "ARTIFACT-FACT-PATH-GATE|PASS" -ForegroundColor Green
Write-Host "TMDL-UTF8-NOBOM-GATE|PASS" -ForegroundColor Green
Write-Host "RunnerPaths=0"
Write-Host "BomFiles=0"
Write-Host "CsvExists=True"

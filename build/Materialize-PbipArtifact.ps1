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

# STEP 1: Read TMDL.
$tmdlFiles = @(Get-ChildItem $SemanticModelRoot -Recurse -Filter "*.tmdl" -File)
$replacementRoot = [IO.Path]::GetFullPath($DataRoot).TrimEnd('\','/')
$replacementCount = 0
$factReplacementCount = 0
$dataFolderReferenceCount = 0
$runnerDataPathCount = 0
$placeholderReplacementCount = 0

# STEP 2: Resolve the shared DataFolder expression first.
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
    $dataFolderValue = $replacementRoot
}

$dataFolderValue = $dataFolderValue.Replace('/', '\').TrimEnd('\')

if (Test-Path $expressionsPath -PathType Leaf) {
    $expressionsText = [IO.File]::ReadAllText($expressionsPath)
    $materializedExpressions = [regex]::Replace(
        $expressionsText,
        '(?im)^(\s*expression\s+DataFolder\s*=\s*")[^"]*(".*)$',
        '${1}' + $replacementRoot + '${2}'
    )
    if ($materializedExpressions -ne $expressionsText) {
        [IO.File]::WriteAllText($expressionsPath, $materializedExpressions, $utf8NoBom)
        $replacementCount++
    }
}

# STEP 3/4: Directly normalize a legitimate Windows GitHub runner data file.
# The matcher is intentionally applied to the resulting File.Contents(...) path,
# not to arbitrary text. Only X:\a\, X:\_work\, and X:\actions\ are eligible,
# and the matched path must contain \data\ followed by a CSV filename.
$runnerWindowsDataFilePattern = '(?i)File\.Contents\(\s*"(?<runnerPath>[A-Z]:\\+(?:a|_work|actions)\\+[^\r\n"]*?\\+data\\+(?<fileName>[^\\/\r\n"]+\.csv))"\s*\)'

# STEP 5: Map the known placeholder and DataFolder expressions.
$fileContentsDataFolderPattern = '(?i)File\.Contents\(\s*DataFolder\s*&\s*"([^"]+)"\s*\)'
$factPattern = '(?i)File\.Contents\("(?:[^"\r\n]*[\\/])?Fact_Pipeline_SampleData\.csv"\)'

foreach ($file in $tmdlFiles) {
    # Read TMDL before any transformation.
    $text = [IO.File]::ReadAllText($file.FullName)
    $updated = $text

    # Normalize the entire absolute Windows runner data path directly.
    $updated = [regex]::Replace(
        $updated,
        $runnerWindowsDataFilePattern,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)

            $fileName = $match.Groups['fileName'].Value
            $targetPath = [IO.Path]::GetFullPath((Join-Path $DataRoot $fileName))

            if (!(Test-Path $targetPath -PathType Leaf)) {
                throw "Runner data-path normalization failed: '$fileName' does not exist at '$targetPath'."
            }

            $script:runnerDataPathCount++
            return 'File.Contents("' + $targetPath + '")'
        }
    )

    # Map the known placeholder root only. Arbitrary absolute paths are not rewritten.
    if ($updated.Contains($PlaceholderRoot)) {
        $updated = $updated.Replace($PlaceholderRoot, $replacementRoot)
        $placeholderReplacementCount++
    }

    # Resolve any remaining shared DataFolder File.Contents expression directly
    # against the artifact-local data directory.
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

    [IO.File]::WriteAllText($file.FullName, $updated, $utf8NoBom)
}

# STEP 6: Verify every materialized CSV path exists before declaring the gate passed.
foreach ($file in $tmdlFiles) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $fileMatches = [regex]::Matches($text, '(?i)File\.Contents\(\s*"([^"]+\.csv)"\s*\)')
    foreach ($match in $fileMatches) {
        $csvPath = $match.Groups[1].Value
        if (!(Test-Path $csvPath -PathType Leaf)) {
            throw "CSV verification failed: File.Contents path '$csvPath' referenced by '$($file.FullName)' does not exist."
        }
    }
}

if (!(Test-Path $expectedFact -PathType Leaf)) {
    throw "Materialization CSV verification failed: Fact source file was not found: $expectedFact"
}

# STEP 7: STRICT runner-path and placeholder gates.
# Artifact-local paths are allowed even when the artifact itself lives under
# D:\a\... on a GitHub runner. Any runner path outside the artifact-local data
# paths remains fatal.
foreach ($file in $tmdlFiles) {
    $text = [IO.File]::ReadAllText($file.FullName)

    if ($text -match [regex]::Escape($PlaceholderRoot)) {
        throw "Artifact materialization failed: placeholder remains in '$($file.FullName)'."
    }

    # Inspect every resulting File.Contents(...) path. A path is runner-specific
    # only when it matches the direct runner-data matcher and is not the current
    # artifact-local data root. This avoids falsely classifying
    # D:\a\...\DesktopValidation\PBIP\data\*.csv as a surviving CI source path.
    $fileContentMatches = [regex]::Matches(
        $text,
        '(?i)File\.Contents\(\s*"([^"]+\.csv)"\s*\)'
    )
    $artifactLocalFileContents = [Collections.Generic.List[string]]::new()
    foreach ($match in $fileContentMatches) {
        $csvPath = [IO.Path]::GetFullPath($match.Groups[1].Value).TrimEnd('\','/')
        $isArtifactLocal = $csvPath.Equals($replacementRoot, [StringComparison]::OrdinalIgnoreCase) -or
                           $csvPath.StartsWith($replacementRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
                           $csvPath.StartsWith($replacementRoot + '/', [StringComparison]::OrdinalIgnoreCase)

        if ($isArtifactLocal) {
            $artifactLocalFileContents.Add($match.Value)
        }

        if (!$isArtifactLocal -and $csvPath -match '(?i)^[A-Z]:\\+(?:a|_work|actions)\\+') {
            throw "Artifact materialization failed: runner-specific File.Contents path remains in '$($file.FullName)': $csvPath"
        }
    }

    # Remove the known-good artifact-local File.Contents paths before the broad
    # textual runner-path gate. This preserves the strict gate for every other
    # runner path without flagging the artifact's own local path merely because
    # DesktopValidation happens to reside below D:\a\ on Windows CI or
    # /home/runner on Linux validation hosts.
    $runnerGateText = $text
    foreach ($artifactLocalFileContentsExpression in $artifactLocalFileContents) {
        $runnerGateText = $runnerGateText.Replace($artifactLocalFileContentsExpression, '')
    }

    foreach ($runnerPattern in $RunnerPathPatterns) {
        if ($runnerGateText -match $runnerPattern) {
            throw "Artifact materialization failed: runner-specific path remains in '$($file.FullName)'. Pattern: $runnerPattern"
        }
    }

    if ($text -match '(?i)File\.Contents\(\s*DataFolder\b') {
        throw "Artifact materialization failed: unresolved DataFolder reference remains in '$($file.FullName)'."
    }
}

if (Test-Path $expressionsPath -PathType Leaf) {
    $materializedExpressionsText = [IO.File]::ReadAllText($expressionsPath)
    $materializedDataFolderMatch = [regex]::Match(
        $materializedExpressionsText,
        '(?im)^\s*expression\s+DataFolder\s*=\s*"((?:""|[^"\r\n])*)"'
    )
    if ($materializedDataFolderMatch.Success) {
        $materializedDataFolderValue = $materializedDataFolderMatch.Groups[1].Value.Replace('""', '"').Replace('/', '\').TrimEnd('\')
        if ($materializedDataFolderValue -ne $replacementRoot) {
            throw "Artifact materialization failed: DataFolder declaration does not resolve to artifact data root '$replacementRoot'. Actual: '$materializedDataFolderValue'"
        }
    }
}

& (Join-Path $PSScriptRoot 'Assert-DateVariationHierarchies.ps1') -PbipRoot $ArtifactRoot

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

$platformFiles = @(
    Get-ChildItem $ArtifactRoot -Recurse -Force -File |
        Where-Object { $_.Name -eq '.platform' }
)
if ($platformFiles.Count -lt 2) {
    throw "Artifact materialization failed: expected both PBIP and semantic-model .platform files; found $($platformFiles.Count)."
}

# STEP 8: Materialization gate. Only after this PASS may the workflow enter Step 2.
Write-Host "PBIP artifact materialized for Desktop." -ForegroundColor Green
Write-Host "Artifact root: $ArtifactRoot"
Write-Host "Data root: $DataRoot"
Write-Host "Resolved DataFolder: $dataFolderValue"
Write-Host "TMDL files materialized: $replacementCount"
Write-Host "Runner data-path normalizations: $runnerDataPathCount"
Write-Host "Placeholder replacements: $placeholderReplacementCount"
Write-Host "DataFolder File.Contents replacements: $dataFolderReferenceCount"
Write-Host "Fact path replacements: $factReplacementCount"
Write-Host "Fact source: $expectedFactPath"
Write-Host "DATA-MATERIALIZATION-GATE|PASS|RunnerPaths=0|DataFolderResolved=PASS|CsvExists=True" -ForegroundColor Green
Write-Host "ARTIFACT-FACT-PATH-GATE|PASS" -ForegroundColor Green
Write-Host "TMDL-UTF8-NOBOM-GATE|PASS" -ForegroundColor Green
Write-Host "DataFolderReferences=0" -ForegroundColor Green
Write-Host "PlatformFiles=$($platformFiles.Count)"
Write-Host "PlatformFiles=PASS" -ForegroundColor Green
Write-Host "RunnerPaths=0"
Write-Host "BomFiles=0"
Write-Host "CsvExists=True"

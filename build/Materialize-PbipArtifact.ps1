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
$replacementRoot = $DataRoot.TrimEnd('\')
$replacementCount = 0
$factReplacementCount = 0
$dataFolderReferenceCount = 0
$runnerDataPathCount = 0
$placeholderReplacementCount = 0

# STEP 2: Resolve the shared DataFolder expression first. The generated model may
# define DataFolder in expressions.tmdl and consume it from table partitions such as:
#   File.Contents(DataFolder & "\\Dim_Category.csv")
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

# Materialize the DataFolder declaration itself to the artifact-local data directory.
# This is deliberately done before dependent File.Contents expressions are rewritten.
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

# STEP 3: Detect runner absolute paths.
# STEP 4: Normalize ONLY legitimate Windows runner paths that point into a data
# directory. The matcher is intentionally anchored to a Windows drive root and
# requires the path segment \data\ immediately before the CSV filename.
# Examples accepted:
#   D:\a\repo\repo\data\Dim_Category.csv
#   C:\a\_work\repo\repo\data\Fact_Pipeline_SampleData.csv
# The strict RunnerPathPatterns gate below remains unchanged and rejects every
# runner path that is not normalized by this explicit data-file mapping.
$runnerWindowsDataFilePattern = '(?i)(?<runnerRoot>[A-Z]:\\(?:[^\r\n"\\]+\\)*?)(?:_work\\[^\r\n"\\]+\\[^\r\n"\\]+\\|a\\[^\r\n"\\]+\\[^\r\n"\\]+\\|actions\\[^\r\n"\\]+\\)data\\(?<fileName>[^\\/\r\n"]+\.csv)'
$runnerWindowsDataFilePatternSimple = '(?i)[A-Z]:\\(?:[^\r\n"\\]+\\)*data\\(?<fileName>[^\\/\r\n"]+\.csv)'
$runnerUnixDataFilePattern = '(?i)(?:/home/runner/[^\r\n"]*/data/)(?<fileName>[^/\r\n"]+\.csv)'

# STEP 5: Map placeholder paths and runner data paths, then resolve DataFolder-based
# File.Contents expressions. No other runner path is permitted to be transformed.
$fileContentsDataFolderPattern = '(?i)File\.Contents\(\s*DataFolder\s*&\s*"([^"]+)"\s*\)'
$factPattern = '(?i)File\.Contents\("(?:[^"\r\n]*[\\/])?Fact_Pipeline_SampleData\.csv"\)'

foreach ($file in $tmdlFiles) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $updated = $text

    # Normalize a legitimate GitHub-hosted Windows runner data path. The first
    # matcher handles the standard GitHub Actions D:\a\<repo>\<repo>\data path;
    # the simple matcher also handles equivalent C:\a\...\data paths. Both
    # require a CSV filename directly under a data directory.
    $updated = [regex]::Replace(
        $updated,
        $runnerWindowsDataFilePattern,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            $fileName = $match.Groups['fileName'].Value
            $targetPath = [IO.Path]::GetFullPath((Join-Path $DataRoot $fileName))
            if (!(Test-Path $targetPath -PathType Leaf)) {
                throw "Runner data-path normalization failed: '$fileName' was referenced by '$($file.FullName)' but '$targetPath' does not exist in the artifact data folder."
            }
            $script:runnerDataPathCount++
            return $targetPath
        }
    )

    $updated = [regex]::Replace(
        $updated,
        $runnerWindowsDataFilePatternSimple,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            $fileName = $match.Groups['fileName'].Value
            $targetPath = [IO.Path]::GetFullPath((Join-Path $DataRoot $fileName))
            if (!(Test-Path $targetPath -PathType Leaf)) {
                throw "Runner data-path normalization failed: '$fileName' was referenced by '$($file.FullName)' but '$targetPath' does not exist in the artifact data folder."
            }
            $script:runnerDataPathCount++
            return $targetPath
        }
    )

    $updated = [regex]::Replace(
        $updated,
        $runnerUnixDataFilePattern,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            $fileName = $match.Groups['fileName'].Value
            $targetPath = [IO.Path]::GetFullPath((Join-Path $DataRoot $fileName))
            if (!(Test-Path $targetPath -PathType Leaf)) {
                throw "Runner data-path normalization failed: '$fileName' was referenced by '$($file.FullName)' but '$targetPath' does not exist in the artifact data folder."
            }
            $script:runnerDataPathCount++
            return $targetPath
        }
    )

    # Map the known placeholder root only. Arbitrary absolute paths are not rewritten.
    if ($updated.Contains($PlaceholderRoot)) {
        $updated = $updated.Replace($PlaceholderRoot, $replacementRoot)
        $placeholderReplacementCount++
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

# STEP 7: Strict runner-path and placeholder gates. These remain intentionally
# unchanged in policy: ANY runner-specific path left after normalization is fatal.
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

    if ($text -match '(?i)File\.Contents\(\s*DataFolder\b') {
        throw "Artifact materialization failed: unresolved DataFolder reference remains in '$($file.FullName)'."
    }
}

# Verify the shared DataFolder declaration, when present, is artifact-local.
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

$platformFiles = @(Get-ChildItem $ArtifactRoot -Recurse -Filter ".platform" -File)
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

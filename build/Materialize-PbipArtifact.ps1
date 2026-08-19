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
    throw "Artifact data directory was not found: $DataRoot."
}

$expectedFact = Join-Path $DataRoot "Fact_Pipeline_SampleData.csv"
$expectedCategory = Join-Path $DataRoot "Dim_Category.csv"
$expectedFactPath = [IO.Path]::GetFullPath($expectedFact)
$expectedCategoryPath = [IO.Path]::GetFullPath($expectedCategory)
$factPath = Join-Path $SemanticModelRoot "definition\tables\Fact_Pipeline_SampleData.tmdl"
$categoryPath = Join-Path $SemanticModelRoot "definition\tables\Dim_Category.tmdl"

foreach ($source in @($expectedFact,$expectedCategory)) {
    if (!(Test-Path $source -PathType Leaf)) {
        throw "Artifact source file was not found: $source"
    }
}
foreach ($table in @($factPath,$categoryPath)) {
    if (!(Test-Path $table -PathType Leaf)) {
        throw "Materialized table TMDL was not found: $table"
    }
}

$tmdlFiles = @(Get-ChildItem $SemanticModelRoot -Recurse -Filter "*.tmdl" -File)
$replacementRoot = $DataRoot.TrimEnd('\')
$replacementCount = 0
$factReplacementCount = 0
$categoryReplacementCount = 0

# Resolve the shared DataFolder expression first. PBIP templates commonly use:
#   File.Contents(DataFolder & "\Dim_Category.csv")
# or:
#   File.Contents(DataFolder & "\Fact_Pipeline_SampleData.csv")
# The Desktop validation copy must contain concrete local paths before any
# downstream validation/visual checks run.
$dataFolderExpressionPattern = '(?i)\bDataFolder\s*&\s*"'

# Materialize every supported CSV File.Contents expression to the artifact-local
# data folder. This deliberately handles both the shared DataFolder expression and
# already-materialized absolute/placeholder paths.
$csvPattern = '(?i)File\.Contents\("(?:[^"\r\n]*[\\/])?([^"\\/]+\.csv)"\)'

foreach ($file in $tmdlFiles) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $updated = $text

    if ($updated.Contains($PlaceholderRoot)) {
        $updated = $updated.Replace($PlaceholderRoot, $replacementRoot)
    }

    # First resolve the shared M expression variable without changing the
    # surrounding query shape. This is the critical materialization boundary.
    $updated = [regex]::Replace(
        $updated,
        $dataFolderExpressionPattern,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            return '"' + $replacementRoot + '" & "'
        }
    )

    $updated = [regex]::Replace(
        $updated,
        $csvPattern,
        [System.Text.RegularExpressions.MatchEvaluator]{
            param($match)
            $fileName = $match.Groups[1].Value
            $sourcePath = Join-Path $DataRoot $fileName
            $sourcePath = [IO.Path]::GetFullPath($sourcePath)

            if (!(Test-Path $sourcePath -PathType Leaf)) {
                throw "Materialization failed: File.Contents references '$fileName' but '$sourcePath' does not exist in the artifact data folder."
            }

            if ($fileName -ieq 'Fact_Pipeline_SampleData.csv') {
                $script:factReplacementCount++
            }
            if ($fileName -ieq 'Dim_Category.csv') {
                $script:categoryReplacementCount++
            }
            $script:replacementCount++
            return 'File.Contents("' + $sourcePath + '")'
        }
    )

    if ($updated -ne $text) {
        $replacementCount++
    }

    [IO.File]::WriteAllText($file.FullName, $updated, $utf8NoBom)
}

# Enforce placeholder and runner-path gates after materialization.
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

    if ($text -match $dataFolderExpressionPattern) {
        throw "Artifact materialization failed: unresolved shared DataFolder expression remains in '$($file.FullName)'."
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
$categoryText = [IO.File]::ReadAllText($categoryPath)
if ($factText -notmatch [regex]::Escape($expectedFactPath)) {
    throw "Materialized Fact partition does not contain the artifact-local data path '$expectedFactPath'."
}
if ($categoryText -notmatch [regex]::Escape($expectedCategoryPath)) {
    throw "Materialized Dim_Category partition does not contain the artifact-local data path '$expectedCategoryPath'."
}

Write-Host "PBIP artifact materialized for Desktop." -ForegroundColor Green
Write-Host "Artifact root: $ArtifactRoot"
Write-Host "Data root: $DataRoot"
Write-Host "TMDL files materialized: $replacementCount"
Write-Host "Fact path replacements: $factReplacementCount"
Write-Host "Dim_Category path replacements: $categoryReplacementCount"
Write-Host "Fact source: $expectedFactPath"
Write-Host "Dim_Category source: $expectedCategoryPath"
Write-Host "DATA-MATERIALIZATION-GATE|PASS|Fact=PASS|Dim_Category=PASS|DataFolderResolved=PASS" -ForegroundColor Green
Write-Host "ARTIFACT-FACT-PATH-GATE|PASS" -ForegroundColor Green
Write-Host "TMDL-UTF8-NOBOM-GATE|PASS" -ForegroundColor Green
Write-Host "RunnerPaths=0"
Write-Host "BomFiles=0"
Write-Host "CsvExists=True"

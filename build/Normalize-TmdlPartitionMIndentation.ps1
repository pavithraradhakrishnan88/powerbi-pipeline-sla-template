[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SemanticModelRoot
)

$ErrorActionPreference = 'Stop'
$SemanticModelRoot = [IO.Path]::GetFullPath($SemanticModelRoot)

$tablesRoot = Join-Path $SemanticModelRoot 'definition\tables'
if (!(Test-Path $tablesRoot -PathType Container)) {
    throw "TMDL tables directory not found: $tablesRoot"
}

$files = @(Get-ChildItem $tablesRoot -Filter '*.tmdl' -File)
if ($files.Count -eq 0) {
    throw "No generated table TMDL files found under $tablesRoot"
}

$repoRoot = Split-Path (Split-Path (Split-Path $SemanticModelRoot -Parent) -Parent) -Parent
$artifactSemanticModelRoot = Join-Path $repoRoot 'artifacts\Pipeline_SLA_Tracker.SemanticModel'
$semanticModelRoots = @($SemanticModelRoot)
if (Test-Path $artifactSemanticModelRoot -PathType Container) {
    $semanticModelRoots += $artifactSemanticModelRoot
}

foreach ($root in $semanticModelRoots) {
    $currentTablesRoot = Join-Path $root 'definition\tables'
    if (!(Test-Path $currentTablesRoot -PathType Container)) { continue }

    $currentFiles = @(Get-ChildItem $currentTablesRoot -Filter '*.tmdl' -File)
    foreach ($file in $currentFiles) {
        # The canonical measure table contains DAX expressions, so a standalone
        # search for four-tab `let` lines can touch valid measure content even
        # though the file contains no partition M source that needs normalization.
        if ($file.Name -eq '_Measure Table.tmdl') {
            continue
        }

        $text = Get-Content -Raw $file.FullName
        $original = $text

        # Power BI Desktop 2.156.951.0 expects the M source block to be nested
        # directly below `source =`. Normalize only the generated partition
        # indentation; do not change the M expression itself.
        $text = [regex]::Replace($text, '(?m)^\t{4}let\r?$', "`t`t`tlet")
        $text = [regex]::Replace($text, '(?m)^\t{4}    (Source = .*)$', "`t`t`t`t`$1")
        $text = [regex]::Replace($text, '(?m)^\t{4}    (#\"Promoted Headers\" = .*)$', "`t`t`t`t`$1")
        $text = [regex]::Replace($text, '(?m)^\t{3}in\r?$', "`t`t`t in".Replace(' ', ''))
        $text = [regex]::Replace($text, '(?m)^\t{4}    (#\"Promoted Headers\"$)', "`t`t`t`t`$1")
        $text = [regex]::Replace($text, '(?m)^\t{4}    (Source$)', "`t`t`t`t`$1")

        if ($text -ne $original) {
            [IO.File]::WriteAllText($file.FullName, $text, [Text.UTF8Encoding]::new($false))
            Write-Host "TMDL-PARTITION-NORMALIZED|$($root)|$($file.Name)"
        }
    }
}

# Fail closed if the Desktop-incompatible four-tab `let` form remains.
$remaining = @()
foreach ($root in $semanticModelRoots) {
    $currentTablesRoot = Join-Path $root 'definition\tables'
    if (!(Test-Path $currentTablesRoot -PathType Container)) { continue }
    $remaining += @(
        Get-ChildItem $currentTablesRoot -Filter '*.tmdl' -File |
            Where-Object { $_.Name -ne '_Measure Table.tmdl' } |
            Select-String -Pattern '^\t{4}let\r?$' -CaseSensitive
    )
}
if ($remaining.Count -gt 0) {
    throw "Desktop-incompatible partition indentation remains in $($remaining.Count) location(s)."
}

Write-Host "TMDL-PARTITION-INDENTATION-GATE|PASS|Files=$($files.Count)"
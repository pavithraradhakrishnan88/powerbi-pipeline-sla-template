[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SemanticModelRoot
)

$ErrorActionPreference = 'Stop'

$tablesRoot = Join-Path $SemanticModelRoot 'definition\tables'
if (!(Test-Path $tablesRoot -PathType Container)) {
    throw "TMDL tables directory not found: $tablesRoot"
}

$files = @(Get-ChildItem $tablesRoot -Filter '*.tmdl' -File)
if ($files.Count -eq 0) {
    throw "No generated table TMDL files found under $tablesRoot"
}

foreach ($file in $files) {
    $text = Get-Content -Raw $file.FullName
    $original = $text

    # Power BI Desktop 2.156.951.0 expects the M source block to be nested
    # directly below `source =`. Normalize only the generated partition
    # indentation; do not change the M expression itself.
    $text = [regex]::Replace($text, '(?m)^\t{4}let$', "`t`t`tlet")
    $text = [regex]::Replace($text, '(?m)^\t{4}    (Source = .*)$', "`t`t`t`t`$1")
    $text = [regex]::Replace($text, '(?m)^\t{4}    (#\"Promoted Headers\" = .*)$', "`t`t`t`t`$1")
    $text = [regex]::Replace($text, '(?m)^\t{3}in$', "`t`t in".Replace(' ', ''))
    $text = [regex]::Replace($text, '(?m)^\t{4}    (#\"Promoted Headers\"$)', "`t`t`t`t`$1")
    $text = [regex]::Replace($text, '(?m)^\t{4}    (Source$)', "`t`t`t`t`$1")

    if ($text -ne $original) {
        [IO.File]::WriteAllText($file.FullName, $text, [Text.UTF8Encoding]::new($false))
        Write-Host "TMDL-PARTITION-NORMALIZED|$($file.Name)"
    }
}

# Fail closed if the Desktop-incompatible four-tab `let` form remains.
$remaining = @(
    Get-ChildItem $tablesRoot -Filter '*.tmdl' -File |
        Select-String -Pattern '^\t{4}let$' -CaseSensitive
)
if ($remaining.Count -gt 0) {
    throw "Desktop-incompatible partition indentation remains in $($remaining.Count) location(s)."
}

Write-Host "TMDL-PARTITION-INDENTATION-GATE|PASS|Files=$($files.Count)"

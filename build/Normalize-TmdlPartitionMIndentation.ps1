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
        $text = Get-Content -Raw $file.FullName
        $original = $text
        $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
        $lines = $text.Replace("`r`n", "`n").Replace("`r", "`n").Split("`n")

        for ($i = 0; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -notmatch '^[ \t]*partition\s+\S+\s*=\s*m\s*$') {
                continue
            }

            $partitionIndent = [regex]::Match($lines[$i], '^[ \t]*').Value
            $partitionChildIndent = $partitionIndent + "`t"
            $sourceIndex = -1

            for ($j = $i + 1; $j -lt $lines.Length; $j++) {
                $candidate = $lines[$j].Trim()
                $candidateIndent = [regex]::Match($lines[$j], '^[ \t]*').Value
                if ($candidate -match '^source\s*=') {
                    $sourceIndex = $j
                    break
                }

                if ($candidate.Length -gt 0 -and $candidateIndent.Length -le $partitionIndent.Length -and $candidate -match '^(?:partition|column|measure|hierarchy|calculationGroup|annotation)\b') {
                    break
                }
            }

            if ($sourceIndex -lt 0) {
                continue
            }

            $normalizedSource = $partitionChildIndent + $lines[$sourceIndex].Trim()
            if ($lines[$sourceIndex] -ne $normalizedSource) {
                $lines[$sourceIndex] = $normalizedSource
            }

            if ($lines[$sourceIndex].Trim() -ne 'source =') {
                continue
            }

            $expressionEnd = $sourceIndex + 1
            while ($expressionEnd -lt $lines.Length) {
                $candidate = $lines[$expressionEnd].Trim()
                $candidateIndent = [regex]::Match($lines[$expressionEnd], '^[ \t]*').Value
                if ($candidate.Length -gt 0 -and $candidateIndent.Length -le $partitionIndent.Length -and $candidate -match '^(?:partition|column|measure|hierarchy|calculationGroup|annotation)\b') {
                    break
                }

                $expressionEnd++
            }

            $expressionIndent = $partitionChildIndent + "`t"
            $nestedIndent = $expressionIndent + "`t"

            for ($j = $sourceIndex + 1; $j -lt $expressionEnd; $j++) {
                $trimmed = $lines[$j].Trim()
                if ($trimmed.Length -eq 0) {
                    continue
                }

                $normalizedLine = if ($trimmed -ceq 'let' -or $trimmed -ceq 'in') {
                    $expressionIndent + $trimmed
                }
                else {
                    $nestedIndent + $trimmed
                }

                if ($lines[$j] -ne $normalizedLine) {
                    $lines[$j] = $normalizedLine
                }
            }

            $i = $expressionEnd - 1
        }

        $text = [string]::Join($newline, $lines)

        if ($text -ne $original) {
            [IO.File]::WriteAllText($file.FullName, $text, [Text.UTF8Encoding]::new($false))
            Write-Host "TMDL-PARTITION-NORMALIZED|$($root)|$($file.Name)"
        }
    }
}

# Fail closed if an M partition still contains source-expression lines that are
# not nested inside the source block.
$remaining = @()
foreach ($root in $semanticModelRoots) {
    $currentTablesRoot = Join-Path $root 'definition\tables'
    if (!(Test-Path $currentTablesRoot -PathType Container)) { continue }
    $remaining += @(
        Get-ChildItem $currentTablesRoot -Filter '*.tmdl' -File |
            Select-String -Pattern '^[ \t]{0,2}(?:let|in|Source\s*=|#"Promoted Headers")\b' -CaseSensitive
    )
}
if ($remaining.Count -gt 0) {
    throw "Desktop-incompatible partition indentation remains in $($remaining.Count) location(s)."
}

Write-Host "TMDL-PARTITION-INDENTATION-GATE|PASS|Files=$($files.Count)"
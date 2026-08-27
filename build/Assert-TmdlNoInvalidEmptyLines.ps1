[CmdletBinding()]
param(
    [string]$PbipRoot = (Join-Path $PSScriptRoot '..\BuildResult\PBIP')
)

$ErrorActionPreference = 'Stop'
$resolvedRoot = [IO.Path]::GetFullPath($PbipRoot)
if (!(Test-Path $resolvedRoot -PathType Container)) {
    throw "TMDL structural gate target does not exist: $resolvedRoot"
}

$definitionRoot = Join-Path $resolvedRoot 'Pipeline_SLA_Tracker.SemanticModel\definition'
$files = @(Get-ChildItem $definitionRoot -Recurse -Filter '*.tmdl' -File | Sort-Object FullName)
if ($files.Count -eq 0) {
    throw "TMDL structural gate found no .tmdl files under the semantic-model definition."
}

$emptyViolations = @()
$contextViolations = @()

function Get-IndentWidth {
    param([string]$Line)
    $prefix = [regex]::Match($Line, '^[ \t]*').Value
    $width = 0
    foreach ($ch in $prefix.ToCharArray()) {
        if ($ch -eq "`t") { $width += 4 } else { $width++ }
    }
    return $width
}

foreach ($file in $files) {
    $lines = @(Get-Content -LiteralPath $file.FullName)

    # Existing InvalidLineType / Empty validation. This remains deliberately
    # narrow: only an empty line immediately before a partition is rejected.
    for ($i = 0; $i -lt $lines.Count - 1; $i++) {
        if ($lines[$i] -notmatch '^\s*$') { continue }
        if ($lines[$i + 1] -match '^\s*partition\s+\S+\s*=') {
            $emptyViolations += [PSCustomObject]@{
                File = $file.FullName
                Line = $i + 1
                NextLine = $i + 2
                ErrorType = 'InvalidLineType / Empty'
                Message = 'Empty TMDL line immediately precedes a partition declaration.'
            }
        }
    }

    # TMDL object-context validation.
    # A partition is valid only as a direct child of the current table. We use
    # indentation as the structural boundary because TMDL is indentation based.
    # This catches the Desktop UnsupportedObjectType failure without deleting
    # or rewriting legitimate calculated-table partitions.
    $tableStack = [Collections.Generic.List[object]]::new()

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\s*$') { continue }

        $indent = Get-IndentWidth $line
        $trimmed = $line.Trim()

        while ($tableStack.Count -gt 0 -and $indent -le [int]$tableStack[$tableStack.Count - 1].Indent) {
            $tableStack.RemoveAt($tableStack.Count - 1)
        }

        if ($trimmed -match '^table\s+(?:''([^'']+)''|([^\s]+))\s*$') {
            $tableName = if ($matches[1]) { $matches[1] } else { $matches[2] }
            $tableStack.Add([PSCustomObject]@{ Name = $tableName; Indent = $indent; Line = $i + 1 })
            continue
        }

        if ($trimmed -match '^partition\s+(\S+)\s*=\s*(.+)$') {
            $partitionName = $matches[1]
            $partitionKind = $matches[2]
            $hasDirectTableParent = $false
            $parentTable = $null

            if ($tableStack.Count -gt 0) {
                $candidate = $tableStack[$tableStack.Count - 1]
                # Direct child means partition indentation is exactly one TMDL
                # child level below its table declaration. In normal generated
                # TMDL this is the same indentation used by columns/measures.
                if ($indent -gt [int]$candidate.Indent) {
                    $hasDirectTableParent = $true
                    $parentTable = $candidate
                }
            }

            if (!$hasDirectTableParent) {
                $contextViolations += [PSCustomObject]@{
                    File = $file.FullName
                    Line = $i + 1
                    Object = $partitionName
                    Parent = if ($tableStack.Count -gt 0) { $tableStack[$tableStack.Count - 1].Name } else { '<none>' }
                    ErrorType = 'UnsupportedObjectType'
                    Message = "Partition '$partitionName' is not a direct child of a table; Desktop may report 'partition is not a supported property in the current context'."
                }
            }
        }
    }
}

if ($emptyViolations.Count -gt 0) {
    foreach ($violation in $emptyViolations) {
        Write-Host "TMDL-EMPTY-LINE-GATE|FAIL|ErrorType=$($violation.ErrorType)|File=$($violation.File)|Line=$($violation.Line)|NextLine=$($violation.NextLine)"
        Write-Host "TMDL-EMPTY-LINE-GATE|DETAIL|$($violation.Message)"
    }
    throw "TMDL empty-line validation failed: $($emptyViolations.Count) InvalidLineType / Empty condition(s) detected."
}

if ($contextViolations.Count -gt 0) {
    foreach ($violation in $contextViolations) {
        Write-Host "TMDL-OBJECT-CONTEXT-GATE|FAIL|$($violation.ErrorType)|File=$($violation.File)|Line=$($violation.Line)|Object=$($violation.Object)|Parent=$($violation.Parent)"
        Write-Host "TMDL-OBJECT-CONTEXT-GATE|DETAIL|$($violation.Message)"
    }
    throw "TMDL object-context validation failed: $($contextViolations.Count) UnsupportedObjectType partition condition(s) detected."
}

Write-Host "TMDL-EMPTY-LINE-GATE|PASS|Files=$($files.Count)|InvalidLineTypeEmpty=0"
Write-Host "TMDL-OBJECT-CONTEXT-GATE|PASS|Files=$($files.Count)|UnsupportedObjectType=0"

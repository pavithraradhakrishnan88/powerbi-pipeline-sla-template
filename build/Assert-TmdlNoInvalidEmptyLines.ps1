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

    # Existing InvalidLineType / Empty validation remains intentionally narrow.
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
    # Every table document must contain a table declaration, and every partition
    # must be a direct child of that table. Partition children such as mode/source
    # must be one indentation level below the partition. We do not rewrite or
    # remove partitions; malformed structure is reported as a gate failure.
    $tableStack = [Collections.Generic.List[object]]::new()
    $tableDeclarations = 0

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
            $tableDeclarations++
            $tableStack.Add([PSCustomObject]@{
                Name = $tableName
                Indent = $indent
                Line = $i + 1
            })
            continue
        }

        if ($trimmed -match '^partition\s+(\S+)\s*=\s*(.+)$') {
            $partitionName = $matches[1]
            $partitionKind = $matches[2]
            $parentTable = $null
            $isDirectChild = $false

            if ($tableStack.Count -gt 0) {
                $candidate = $tableStack[$tableStack.Count - 1]
                # Generated TMDL uses one tab (4 logical spaces) for table
                # properties/partitions. Require exactly one child indentation
                # level rather than merely accepting any deeper indentation.
                if ($indent -eq ([int]$candidate.Indent + 4)) {
                    $parentTable = $candidate
                    $isDirectChild = $true
                }
            }

            if (!$isDirectChild) {
                $contextViolations += [PSCustomObject]@{
                    File = $file.FullName
                    Line = $i + 1
                    Object = $partitionName
                    Parent = if ($parentTable) { $parentTable.Name } elseif ($tableStack.Count -gt 0) { $tableStack[$tableStack.Count - 1].Name } else { '<none>' }
                    ErrorType = 'UnsupportedObjectType'
                    Message = "Partition '$partitionName' at indentation $indent is not a direct table child. Expected table-child indentation of 4 spaces beyond the table declaration. Desktop may report 'partition is not a supported property in the current context'."
                }
                continue
            }

            # Validate the immediate partition body. mode/source are expected to
            # be direct children of the partition. Blank lines are permitted after
            # the declaration only when they are not immediately before another
            # partition (handled by the existing empty-line gate above).
            $partitionEnd = $lines.Count
            for ($j = $i + 1; $j -lt $lines.Count; $j++) {
                if ($lines[$j] -match '^\s*$') { continue }
                $nextIndent = Get-IndentWidth $lines[$j]
                $nextTrimmed = $lines[$j].Trim()
                if ($nextIndent -le $indent) {
                    $partitionEnd = $j
                    break
                }

                if ($nextTrimmed -match '^(mode|source)\b') {
                    if ($nextIndent -ne ($indent + 4)) {
                        $contextViolations += [PSCustomObject]@{
                            File = $file.FullName
                            Line = $j + 1
                            Object = $nextTrimmed
                            Parent = "partition $partitionName"
                            ErrorType = 'UnsupportedObjectType'
                            Message = "Partition child '$nextTrimmed' is not nested directly under partition '$partitionName'."
                        }
                    }
                }
            }
        }
    }

    # A .tmdl document under tables is a table document for this gate. Require
    # an actual table declaration so a partition cannot accidentally become a
    # top-level object after generation/normalization.
    $relative = $file.FullName.Substring($definitionRoot.Length).TrimStart('\\','/')
    if ($relative -like 'tables\*' -and $tableDeclarations -eq 0) {
        $contextViolations += [PSCustomObject]@{
            File = $file.FullName
            Line = 1
            Object = '<table>'
            Parent = '<none>'
            ErrorType = 'UnsupportedObjectType'
            Message = 'Table document contains no table declaration; any partition in this document has no legal table context.'
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
    throw "TMDL object-context validation failed: $($contextViolations.Count) UnsupportedObjectType condition(s) detected."
}

Write-Host "TMDL-EMPTY-LINE-GATE|PASS|Files=$($files.Count)|InvalidLineTypeEmpty=0"
Write-Host "TMDL-OBJECT-CONTEXT-GATE|PASS|Files=$($files.Count)|UnsupportedObjectType=0"

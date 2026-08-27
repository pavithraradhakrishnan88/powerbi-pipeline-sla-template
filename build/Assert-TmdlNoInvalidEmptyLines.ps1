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
if (!(Test-Path $definitionRoot -PathType Container)) {
    throw "TMDL structural gate definition root does not exist: $definitionRoot"
}

$files = @(Get-ChildItem $definitionRoot -Recurse -Filter '*.tmdl' -File | Sort-Object FullName)
if ($files.Count -eq 0) {
    throw "TMDL structural gate found no .tmdl files under the semantic-model definition."
}

$emptyViolations = @()
$contextViolations = @()

function Get-IndentWidth {
    param([AllowEmptyString()][string]$Line)
    $prefix = [regex]::Match($Line, '^[ \t]*').Value
    $width = 0
    foreach ($ch in $prefix.ToCharArray()) {
        if ($ch -eq [char]9) { $width += 4 } else { $width++ }
    }
    return $width
}

foreach ($file in $files) {
    $lines = @(Get-Content -LiteralPath $file.FullName)

    # Preserve the existing narrow InvalidLineType / Empty check.
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

    # TMDL object-context validation. TMDL object hierarchy is represented by
    # indentation. A partition must be a direct child of a table, and mode/source
    # must be direct children of that partition. No partition is removed or
    # rewritten by this gate.
    $tableStack = [Collections.Generic.List[object]]::new()
    $partitionStack = [Collections.Generic.List[object]]::new()
    $tableDeclarations = 0

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\s*$') { continue }

        $indent = Get-IndentWidth $line
        $trimmed = $line.Trim()

        while ($tableStack.Count -gt 0 -and $indent -le [int]$tableStack[$tableStack.Count - 1].Indent) {
            $tableStack.RemoveAt($tableStack.Count - 1)
        }
        while ($partitionStack.Count -gt 0 -and $indent -le [int]$partitionStack[$partitionStack.Count - 1].Indent) {
            $partitionStack.RemoveAt($partitionStack.Count - 1)
        }

        if ($trimmed -match '^table\s+(?:''([^'']+)''|([^\s]+))\s*$') {
            $tableName = if ($matches[1]) { $matches[1] } else { $matches[2] }
            $tableDeclarations++
            $tableStack.Add([PSCustomObject]@{ Name = $tableName; Indent = $indent; Line = $i + 1 })
            continue
        }

        if ($trimmed -match '^partition\s+(\S+)\s*=\s*(.+)$') {
            $partitionName = $matches[1]
            $parentTable = $null
            if ($tableStack.Count -gt 0) {
                $candidate = $tableStack[$tableStack.Count - 1]
                if ($indent -eq ([int]$candidate.Indent + 4)) {
                    $parentTable = $candidate
                }
            }

            if ($null -eq $parentTable) {
                $nearest = if ($tableStack.Count -gt 0) { $tableStack[$tableStack.Count - 1].Name } else { '<none>' }
                $contextViolations += [PSCustomObject]@{
                    File = $file.FullName
                    Line = $i + 1
                    Object = $partitionName
                    Parent = $nearest
                    ErrorType = 'UnsupportedObjectType'
                    Message = "Partition '$partitionName' at indentation $indent is not a direct child of a table."
                }
                continue
            }

            $partitionStack.Add([PSCustomObject]@{
                Name = $partitionName
                Indent = $indent
                Line = $i + 1
                Table = $parentTable.Name
            })
            continue
        }

        if ($partitionStack.Count -gt 0 -and $trimmed -match '^(mode|source)\b') {
            $partition = $partitionStack[$partitionStack.Count - 1]
            if ($indent -ne ([int]$partition.Indent + 4)) {
                $contextViolations += [PSCustomObject]@{
                    File = $file.FullName
                    Line = $i + 1
                    Object = $trimmed
                    Parent = "partition $($partition.Name)"
                    ErrorType = 'UnsupportedObjectType'
                    Message = "Partition child '$trimmed' is not directly nested under partition '$($partition.Name)'."
                }
            }
        }
    }

    # Files below definition/tables are table documents. Require a table
    # declaration so a generated partition cannot become a top-level object.
    $relative = $file.FullName.Substring($definitionRoot.Length).TrimStart([char]92, [char]47)
    if ($relative -like 'tables\*' -and $tableDeclarations -eq 0) {
        $contextViolations += [PSCustomObject]@{
            File = $file.FullName
            Line = 1
            Object = '<table>'
            Parent = '<none>'
            ErrorType = 'UnsupportedObjectType'
            Message = 'Table document contains no table declaration; partitions have no legal table context.'
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

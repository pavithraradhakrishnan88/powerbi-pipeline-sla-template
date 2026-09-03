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
$runnerPathViolations = @()

function Get-IndentWidth {
    param([AllowEmptyString()][string]$Line)
    $prefix = [regex]::Match($Line, '^[ \t]*').Value
    $width = 0
    foreach ($ch in $prefix.ToCharArray()) {
        if ($ch -eq [char]9) { $width += 4 } else { $width++ }
    }
    return $width
}

function Test-IsTableLevelChild {
    param([string]$Trimmed)
    return $Trimmed -cmatch '^(?:column|hierarchy|annotation|measure|calculationItem|expression|partition)\b'
}

function Add-ContextViolation {
    param(
        [string]$File,
        [int]$Line,
        [string]$Object,
        [string]$Parent,
        [string]$Message
    )

    $script:contextViolations += [PSCustomObject]@{
        File = $File
        Line = $Line
        Object = $Object
        Parent = $Parent
        ErrorType = 'UnsupportedObjectType'
        Message = $Message
    }
}

$runnerPattern = '(?i)(?:[A-Z]:\\[^\r\n"]*\\(?:_work|a|actions)\\|/home/runner/|/opt/hostedtoolcache/|/runner/_work/)'

foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    if ($text -match $runnerPattern) {
        $runnerPathViolations += [PSCustomObject]@{
            File = $file.FullName
            ErrorType = 'RunnerSpecificPath'
            Message = 'Generated TMDL contains a CI runner or workspace-specific absolute path.'
        }
    }

    $lines = @($text -split "\r?\n")

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
            $partitionKind = $matches[2].Trim()
            $parentTable = $null
            if ($tableStack.Count -gt 0) {
                $candidate = $tableStack[$tableStack.Count - 1]
                if ($indent -eq ([int]$candidate.Indent + 4)) {
                    $parentTable = $candidate
                }
            }

            if ($null -eq $parentTable) {
                $nearest = if ($tableStack.Count -gt 0) { $tableStack[$tableStack.Count - 1].Name } else { '<none>' }
                Add-ContextViolation -File $file.FullName -Line ($i + 1) -Object $partitionName -Parent $nearest -Message "Partition '$partitionName' at indentation $indent is not a direct child of a table."
                continue
            }

            $partitionStack.Add([PSCustomObject]@{
                Name = $partitionName
                Indent = $indent
                Line = $i + 1
                Table = $parentTable.Name
                Kind = $partitionKind
                HasMode = $false
                HasSource = $false
                SourceLine = 0
                SourceInline = $false
                SourceExpressionIndent = $indent + 8
                SeenLet = $false
                SeenIn = $false
                SawLineAfterIn = $false
                LastToken = ''
            })
            continue
        }

        if ($partitionStack.Count -gt 0) {
            $partition = $partitionStack[$partitionStack.Count - 1]

            if ($trimmed -cmatch '^mode:') {
                if ($indent -ne ([int]$partition.Indent + 4)) {
                    Add-ContextViolation -File $file.FullName -Line ($i + 1) -Object $trimmed -Parent "partition $($partition.Name)" -Message "Partition child '$trimmed' is not directly nested under partition '$($partition.Name)'."
                }

                $partition.HasMode = $true
                $partition.LastToken = 'mode'
                continue
            }

            if ($trimmed -cmatch '^source\s*=') {
                if ($indent -ne ([int]$partition.Indent + 4)) {
                    Add-ContextViolation -File $file.FullName -Line ($i + 1) -Object $trimmed -Parent "partition $($partition.Name)" -Message "Partition child '$trimmed' is not directly nested under partition '$($partition.Name)'."
                }

                $partition.HasSource = $true
                $partition.SourceLine = $i + 1
                $partition.SourceInline = $trimmed -ne 'source ='
                $partition.LastToken = if ($partition.SourceInline) { 'source-inline' } else { 'source-open' }
                continue
            }

            if ([string]$partition.Kind -eq 'm') {
                $expressionIndent = [int]$partition.SourceExpressionIndent

                if ($partition.LastToken -eq 'source-open' -and $trimmed -eq 'let') {
                    if ($indent -ne $expressionIndent) {
                        Add-ContextViolation -File $file.FullName -Line ($i + 1) -Object 'let' -Parent "source of partition $($partition.Name)" -Message "The let line for partition '$($partition.Name)' must be directly nested inside the source expression."
                    }

                    $partition.SeenLet = $true
                    $partition.LastToken = 'let'
                    continue
                }

                if ($partition.SeenLet -and $trimmed -eq 'in') {
                    if ($indent -ne $expressionIndent) {
                        Add-ContextViolation -File $file.FullName -Line ($i + 1) -Object 'in' -Parent "source of partition $($partition.Name)" -Message "The in line for partition '$($partition.Name)' must be directly nested inside the source expression."
                    }

                    $partition.SeenIn = $true
                    $partition.LastToken = 'in'
                    continue
                }

                if ($partition.SeenLet -and $indent -le $expressionIndent) {
                    Add-ContextViolation -File $file.FullName -Line ($i + 1) -Object $trimmed -Parent "source of partition $($partition.Name)" -Message "M expression content in partition '$($partition.Name)' is not nested beneath the source block."
                    continue
                }

                if ($partition.SeenLet -and !$partition.SeenIn) {
                    if (Test-IsTableLevelChild $trimmed) {
                        Add-ContextViolation -File $file.FullName -Line ($i + 1) -Object $trimmed -Parent "source of partition $($partition.Name)" -Message "A TMDL object/property line appeared inside the M let block for partition '$($partition.Name)'."
                    }

                    $partition.LastToken = 'binding'
                    continue
                }

                if ($partition.SeenIn) {
                    if (Test-IsTableLevelChild $trimmed) {
                        Add-ContextViolation -File $file.FullName -Line ($i + 1) -Object $trimmed -Parent "source of partition $($partition.Name)" -Message "A TMDL object/property line appeared where the final M result should be for partition '$($partition.Name)'."
                    }

                    $partition.SawLineAfterIn = $true
                    $partition.LastToken = 'result'
                    continue
                }

                if ($partition.HasSource -and !$partition.SourceInline) {
                    Add-ContextViolation -File $file.FullName -Line ($i + 1) -Object $trimmed -Parent "partition $($partition.Name)" -Message "Unexpected line inside partition '$($partition.Name)' before the M let/source structure was established."
                    continue
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

foreach ($partitionFile in $files) {
    $lines = @(Get-Content -LiteralPath $partitionFile.FullName)
    $activePartition = $null

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\s*$') { continue }

        $indent = Get-IndentWidth $line
        $trimmed = $line.Trim()

        if ($trimmed -cmatch '^partition\s+(\S+)\s*=\s*(.+)$') {
            if ($null -ne $activePartition -and [string]$activePartition.Kind -eq 'm') {
                if (!$activePartition.HasMode) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.Line -Object $activePartition.Name -Parent $activePartition.Table -Message "Partition '$($activePartition.Name)' is missing mode: import." }
                if (!$activePartition.HasSource) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.Line -Object $activePartition.Name -Parent $activePartition.Table -Message "Partition '$($activePartition.Name)' is missing a source property." }
                if ($activePartition.HasSource -and !$activePartition.SourceInline -and !$activePartition.SeenLet) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.SourceLine -Object 'source =' -Parent "partition $($activePartition.Name)" -Message "Partition '$($activePartition.Name)' is missing the let line inside its source expression." }
                if ($activePartition.HasSource -and !$activePartition.SourceInline -and !$activePartition.SeenIn) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.SourceLine -Object 'source =' -Parent "partition $($activePartition.Name)" -Message "Partition '$($activePartition.Name)' is missing the in line inside its source expression." }
                if ($activePartition.SeenIn -and !$activePartition.SawLineAfterIn) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.SourceLine -Object 'source =' -Parent "partition $($activePartition.Name)" -Message "Partition '$($activePartition.Name)' is missing the final M expression after in." }
            }

            $activePartition = [PSCustomObject]@{
                Name = $matches[1]
                Kind = $matches[2].Trim()
                Table = '<unknown>'
                Line = $i + 1
                Indent = $indent
                HasMode = $false
                HasSource = $false
                SourceLine = 0
                SourceInline = $false
                SeenLet = $false
                SeenIn = $false
                SawLineAfterIn = $false
            }
            continue
        }

        if ($null -eq $activePartition) { continue }
        if (Test-IsTableLevelChild $trimmed -and $indent -le [int]$activePartition.Indent) {
            if ([string]$activePartition.Kind -eq 'm') {
                if (!$activePartition.HasMode) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.Line -Object $activePartition.Name -Parent $activePartition.Table -Message "Partition '$($activePartition.Name)' is missing mode: import." }
                if (!$activePartition.HasSource) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.Line -Object $activePartition.Name -Parent $activePartition.Table -Message "Partition '$($activePartition.Name)' is missing a source property." }
                if ($activePartition.HasSource -and !$activePartition.SourceInline -and !$activePartition.SeenLet) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.SourceLine -Object 'source =' -Parent "partition $($activePartition.Name)" -Message "Partition '$($activePartition.Name)' is missing the let line inside its source expression." }
                if ($activePartition.HasSource -and !$activePartition.SourceInline -and !$activePartition.SeenIn) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.SourceLine -Object 'source =' -Parent "partition $($activePartition.Name)" -Message "Partition '$($activePartition.Name)' is missing the in line inside its source expression." }
                if ($activePartition.SeenIn -and !$activePartition.SawLineAfterIn) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.SourceLine -Object 'source =' -Parent "partition $($activePartition.Name)" -Message "Partition '$($activePartition.Name)' is missing the final M expression after in." }
            }

            $activePartition = $null
            if ($trimmed -notmatch '^partition\s+') {
                continue
            }
        }

        if ($trimmed -cmatch '^mode:') { $activePartition.HasMode = $true; continue }
        if ($trimmed -cmatch '^source\s*=') { $activePartition.HasSource = $true; $activePartition.SourceLine = $i + 1; $activePartition.SourceInline = $trimmed -ne 'source ='; continue }
        if ($trimmed -eq 'let') { $activePartition.SeenLet = $true; continue }
        if ($trimmed -eq 'in') { $activePartition.SeenIn = $true; continue }
        if ($activePartition.SeenIn) { $activePartition.SawLineAfterIn = $true }
    }

    if ($null -ne $activePartition -and [string]$activePartition.Kind -eq 'm') {
        if (!$activePartition.HasMode) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.Line -Object $activePartition.Name -Parent $activePartition.Table -Message "Partition '$($activePartition.Name)' is missing mode: import." }
        if (!$activePartition.HasSource) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.Line -Object $activePartition.Name -Parent $activePartition.Table -Message "Partition '$($activePartition.Name)' is missing a source property." }
        if ($activePartition.HasSource -and !$activePartition.SourceInline -and !$activePartition.SeenLet) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.SourceLine -Object 'source =' -Parent "partition $($activePartition.Name)" -Message "Partition '$($activePartition.Name)' is missing the let line inside its source expression." }
        if ($activePartition.HasSource -and !$activePartition.SourceInline -and !$activePartition.SeenIn) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.SourceLine -Object 'source =' -Parent "partition $($activePartition.Name)" -Message "Partition '$($activePartition.Name)' is missing the in line inside its source expression." }
        if ($activePartition.SeenIn -and !$activePartition.SawLineAfterIn) { Add-ContextViolation -File $partitionFile.FullName -Line $activePartition.SourceLine -Object 'source =' -Parent "partition $($activePartition.Name)" -Message "Partition '$($activePartition.Name)' is missing the final M expression after in." }
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

if ($runnerPathViolations.Count -gt 0) {
    foreach ($violation in $runnerPathViolations) {
        Write-Host "TMDL-RUNNER-PATH-GATE|FAIL|$($violation.ErrorType)|File=$($violation.File)"
        Write-Host "TMDL-RUNNER-PATH-GATE|DETAIL|$($violation.Message)"
    }
    throw "TMDL runner/workspace path validation failed: $($runnerPathViolations.Count) violation(s) detected."
}

Write-Host "TMDL-EMPTY-LINE-GATE|PASS|Files=$($files.Count)|InvalidLineTypeEmpty=0"
Write-Host "TMDL-OBJECT-CONTEXT-GATE|PASS|Files=$($files.Count)|UnsupportedObjectType=0"
Write-Host "TMDL-RUNNER-PATH-GATE|PASS|Files=$($files.Count)|RunnerSpecificPaths=0"

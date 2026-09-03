[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PbipRoot,
    [string]$PbipName = "Pipeline_SLA_Tracker"
)

$ErrorActionPreference = "Stop"

function Get-TmdlIndentWidth {
    param([Parameter(Mandatory = $true)][string]$Line)

    $width = 0
    foreach ($character in $Line.ToCharArray()) {
        if ($character -eq "`t") {
            $width += 4
            continue
        }

        if ($character -eq ' ') {
            $width++
            continue
        }

        break
    }

    return $width
}

function ConvertFrom-TmdlIdentifier {
    param([Parameter(Mandatory = $true)][string]$Value)

    $trimmed = $Value.Trim()
    if ($trimmed.Length -ge 2 -and $trimmed[0] -eq "'" -and $trimmed[-1] -eq "'") {
        return $trimmed.Substring(1, $trimmed.Length - 2).Replace("''", "'")
    }

    return $trimmed
}

function Split-TmdlObjectReference {
    param([Parameter(Mandatory = $true)][string]$Reference)

    $inQuotes = $false
    for ($index = 0; $index -lt $Reference.Length; $index++) {
        $character = $Reference[$index]
        if ($character -eq "'") {
            if ($inQuotes -and ($index + 1) -lt $Reference.Length -and $Reference[$index + 1] -eq "'") {
                $index++
                continue
            }

            $inQuotes = -not $inQuotes
            continue
        }

        if ($character -eq '.' -and -not $inQuotes) {
            return [PSCustomObject]@{
                Table = ConvertFrom-TmdlIdentifier $Reference.Substring(0, $index)
                Hierarchy = ConvertFrom-TmdlIdentifier $Reference.Substring($index + 1)
            }
        }
    }

    throw "Invalid TMDL object reference '$Reference'. Expected <table>.<hierarchy>."
}

$expectedColumns = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$expectedColumns["ScheduledStart"] = "LocalDateTable_9043e032-67a4-45e7-bf88-28fec57966b9"
$expectedColumns["ActualStart"] = "LocalDateTable_ac53fd01-924f-4452-a3f3-a0f9a1df973c"
$expectedColumns["ScheduledEnd"] = "LocalDateTable_d4583ee4-86f0-47d3-96ae-303f0614bf0d"
$expectedColumns["ActualEnd"] = "LocalDateTable_1c11b445-5c42-44a6-9f02-0bc10f99ee27"

$pbipRoot = [IO.Path]::GetFullPath($PbipRoot)
$semanticModelRoot = Join-Path $pbipRoot "$PbipName.SemanticModel"
$tablesRoot = Join-Path $semanticModelRoot "definition\tables"

if (!(Test-Path $tablesRoot -PathType Container)) {
    throw "Date variation validation failed: semantic-model tables directory was not found: $tablesRoot"
}

$tableFiles = @(Get-ChildItem $tablesRoot -Filter '*.tmdl' -File | Sort-Object Name)
if ($tableFiles.Count -eq 0) {
    throw "Date variation validation failed: no table TMDL files were found under '$tablesRoot'."
}

$availableHierarchies = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$tableDeclarations = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($file in $tableFiles) {
    $tableName = $null
    foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        if ($null -eq $tableName -and $trimmed -match '^table\s+(.+)$') {
            $tableName = ConvertFrom-TmdlIdentifier $matches[1]
            if ($tableDeclarations.ContainsKey($tableName)) {
                throw "Date variation validation failed: duplicate table '$tableName' found in '$($tableDeclarations[$tableName])' and '$($file.FullName)'."
            }

            $tableDeclarations[$tableName] = $file.FullName
            continue
        }

        if ($null -ne $tableName -and $trimmed -match '^hierarchy\s+(.+)$') {
            $hierarchyName = ConvertFrom-TmdlIdentifier $matches[1]
            $null = $availableHierarchies.Add("$tableName.$hierarchyName")
        }
    }
}

$results = [System.Collections.Generic.List[object]]::new()

foreach ($file in $tableFiles) {
    $tableName = $null
    $currentColumn = $null
    $currentColumnIndent = -1
    $currentVariation = $null
    $currentVariationIndent = -1

    foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
        $trimmed = $line.Trim()
        $indent = Get-TmdlIndentWidth $line

        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        if ($trimmed -match '^table\s+(.+)$') {
            $tableName = ConvertFrom-TmdlIdentifier $matches[1]
            $currentColumn = $null
            $currentVariation = $null
            $currentColumnIndent = -1
            $currentVariationIndent = -1
            continue
        }

        if ($null -ne $currentVariation -and $indent -le $currentVariationIndent -and $trimmed -match '^(?:variation|column|measure|hierarchy|partition|annotation|calculationGroup|calculationItem)\b') {
            $currentVariation = $null
            $currentVariationIndent = -1
        }

        if ($null -ne $currentColumn -and $indent -le $currentColumnIndent -and $trimmed -match '^(?:column|measure|hierarchy|partition|annotation|calculationGroup|calculationItem)\b') {
            $currentColumn = $null
            $currentVariation = $null
            $currentColumnIndent = -1
            $currentVariationIndent = -1
        }

        if ($trimmed -match '^column\s+(.+)$') {
            $currentColumn = ConvertFrom-TmdlIdentifier $matches[1]
            $currentColumnIndent = $indent
            $currentVariation = $null
            $currentVariationIndent = -1
            continue
        }

        if ($null -ne $currentColumn -and $trimmed -match '^variation\s+(.+)$') {
            $currentVariation = ConvertFrom-TmdlIdentifier $matches[1]
            $currentVariationIndent = $indent
            continue
        }

        if ($null -ne $currentColumn -and $null -ne $currentVariation -and $trimmed -match '^defaultHierarchy:\s*(.+)$') {
            $reference = $matches[1].Trim()
            $reason = $null
            $resolvedTarget = $null

            try {
                $target = Split-TmdlObjectReference $reference
                $resolvedTarget = "$($target.Table).$($target.Hierarchy)"

                if (!$tableDeclarations.ContainsKey($target.Table)) {
                    $reason = "missing table '$($target.Table)'"
                }
                elseif (!$availableHierarchies.Contains($resolvedTarget)) {
                    $reason = "missing hierarchy '$($target.Hierarchy)' on table '$($target.Table)'"
                }
            }
            catch {
                $reason = $_.Exception.Message
            }

            $results.Add([PSCustomObject]@{
                Table = $tableName
                Column = $currentColumn
                Variation = $currentVariation
                Reference = $reference
                ResolvedTarget = $resolvedTarget
                Success = [string]::IsNullOrWhiteSpace($reason)
                Reason = $reason
            })
        }
    }
}

$reportedColumns = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($result in $results) {
    $null = $reportedColumns.Add($result.Column)
}

$failures = [System.Collections.Generic.List[string]]::new()

foreach ($columnName in $expectedColumns.Keys) {
    $matchingResults = @($results | Where-Object { $_.Column -ieq $columnName })
    if ($matchingResults.Count -eq 0) {
        $message = "DATE-VARIATION-HIERARCHY|$columnName|FAIL|<missing>|No variation.defaultHierarchy was emitted."
        Write-Host $message
        $failures.Add($message)
        continue
    }

    foreach ($result in $matchingResults) {
        $expectedTarget = "$($expectedColumns[$columnName]).Date Hierarchy"
        if ($result.Success -and $result.ResolvedTarget -ine $expectedTarget) {
            $result.Success = $false
            $result.Reason = "resolved hierarchy '$($result.ResolvedTarget)' does not match expected '$expectedTarget'"
        }

        if ($result.Success) {
            Write-Host "DATE-VARIATION-HIERARCHY|$columnName|PASS|$($result.Reference)"
        }
        else {
            $message = "DATE-VARIATION-HIERARCHY|$columnName|FAIL|$($result.Reference)|$($result.Reason)"
            Write-Host $message
            $failures.Add($message)
        }
    }
}

foreach ($result in $results | Where-Object { -not $expectedColumns.ContainsKey($_.Column) }) {
    if ($result.Success) {
        Write-Host "DATE-VARIATION-HIERARCHY|$($result.Column)|PASS|$($result.Reference)"
        continue
    }

    $message = "DATE-VARIATION-HIERARCHY|$($result.Column)|FAIL|$($result.Reference)|$($result.Reason)"
    Write-Host $message
    $failures.Add($message)
}

if ($failures.Count -gt 0) {
    throw "Date variation validation failed:`n - $($failures -join "`n - ")"
}

Write-Host "DATE-VARIATION-HIERARCHY-GATE|PASS|Count=$($results.Count)|Root=$pbipRoot"

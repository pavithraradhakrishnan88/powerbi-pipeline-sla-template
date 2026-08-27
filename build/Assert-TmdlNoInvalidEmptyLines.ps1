[CmdletBinding()]
param(
    [string]$PbipRoot = (Join-Path $PSScriptRoot '..\BuildResult\PBIP')
)

$ErrorActionPreference = 'Stop'
$resolvedRoot = [IO.Path]::GetFullPath($PbipRoot)
if (!(Test-Path $resolvedRoot -PathType Container)) {
    throw "TMDL empty-line gate target does not exist: $resolvedRoot"
}

$files = @(Get-ChildItem (Join-Path $resolvedRoot 'Pipeline_SLA_Tracker.SemanticModel\definition') -Recurse -Filter '*.tmdl' -File)
if ($files.Count -eq 0) {
    throw "TMDL empty-line gate found no .tmdl files under the semantic-model definition."
}

$violations = @()
foreach ($file in $files) {
    $lines = @(Get-Content -LiteralPath $file.FullName)
    for ($i = 0; $i -lt $lines.Count - 1; $i++) {
        if ($lines[$i] -notmatch '^\s*$') { continue }
        if ($lines[$i + 1] -match '^\s*partition\s+\S+\s*=') {
            $violations += [PSCustomObject]@{
                File = $file.FullName
                Line = $i + 1
                NextLine = $i + 2
                ErrorType = 'InvalidLineType / Empty'
                Message = 'Empty TMDL line immediately precedes a partition declaration.'
            }
        }
    }
}

if ($violations.Count -gt 0) {
    foreach ($violation in $violations) {
        Write-Host "TMDL-EMPTY-LINE-GATE|FAIL|ErrorType=$($violation.ErrorType)|File=$($violation.File)|Line=$($violation.Line)|NextLine=$($violation.NextLine)"
        Write-Host "TMDL-EMPTY-LINE-GATE|DETAIL|$($violation.Message)"
    }
    throw "TMDL empty-line validation failed: $($violations.Count) InvalidLineType / Empty condition(s) detected."
}

Write-Host "TMDL-EMPTY-LINE-GATE|PASS|Files=$($files.Count)|InvalidLineTypeEmpty=0"

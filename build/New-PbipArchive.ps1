[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,

    [Parameter(Mandatory = $true)]
    [string]$DestinationPath,

    [string[]]$RequiredEntries = @()
)

$ErrorActionPreference = "Stop"

$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
$DestinationPath = [IO.Path]::GetFullPath($DestinationPath)
$RequiredEntries = @(
    $RequiredEntries |
        ForEach-Object { $_ -split ',' } |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique
)

if (!(Test-Path $SourceRoot -PathType Container)) {
    throw "PBIP archive packaging failed: source root was not found: $SourceRoot"
}

$sourceFiles = @(
    Get-ChildItem -LiteralPath $SourceRoot -Recurse -Force -File |
        ForEach-Object { [IO.Path]::GetRelativePath($SourceRoot, $_.FullName).Replace('\', '/') } |
        Sort-Object -Unique
)

if ($sourceFiles.Count -eq 0) {
    throw "PBIP archive packaging failed: source root contains no files: $SourceRoot"
}

$destinationDirectory = Split-Path -Parent $DestinationPath
if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
}

if (Test-Path $DestinationPath -PathType Leaf) {
    Remove-Item $DestinationPath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $SourceRoot,
    $DestinationPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

$zip = [System.IO.Compression.ZipFile]::OpenRead($DestinationPath)

try {
    $zipEntries = @(
        $zip.Entries |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) } |
            ForEach-Object { $_.FullName.Replace('\', '/') } |
            Sort-Object -Unique
    )
}
finally {
    $zip.Dispose()
}

$fileSetDiff = Compare-Object -ReferenceObject $sourceFiles -DifferenceObject $zipEntries
if ($null -ne $fileSetDiff) {
    $details = ($fileSetDiff | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join '; '
    throw "PBIP archive packaging failed: ZIP file set differs from source root. $details"
}

$missingEntries = @($RequiredEntries | Where-Object { $zipEntries -notcontains $_ })
if ($missingEntries.Count -gt 0) {
    throw "PBIP archive packaging failed: required ZIP entries are missing: $($missingEntries -join ', ')"
}

Write-Host "PBIP-ZIP-GATE|PASS|SourceRoot=$SourceRoot|Destination=$DestinationPath|Files=$($zipEntries.Count)|RequiredEntries=$($RequiredEntries.Count)"

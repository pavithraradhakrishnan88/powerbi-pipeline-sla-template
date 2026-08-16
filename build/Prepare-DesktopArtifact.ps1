param(
    [string]$BuildRoot = "",
    [string]$OutputRoot = "",
    [switch]$Open
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($BuildRoot)) {
    $BuildRoot = Join-Path $repoRoot "BuildResult\PBIP"
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "BuildResult\Desktop"
}

$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$pbipName = "Pipeline_SLA_Tracker"
$sourceProject = Join-Path $BuildRoot "$pbipName.pbip"
$sourceReport = Join-Path $BuildRoot "$pbipName.Report"
$sourceSemanticModel = Join-Path $BuildRoot "$pbipName.SemanticModel"
$dataRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "data"))

if (!(Test-Path $sourceProject -PathType Leaf)) { throw "Generated PBIP project was not found: $sourceProject" }
if (!(Test-Path $sourceReport -PathType Container)) { throw "Generated report was not found: $sourceReport" }
if (!(Test-Path $sourceSemanticModel -PathType Container)) { throw "Generated semantic model was not found: $sourceSemanticModel" }
if (!(Test-Path $dataRoot -PathType Container)) { throw "Local data directory was not found: $dataRoot" }

if (Test-Path $OutputRoot) { Remove-Item $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

Copy-Item $sourceProject (Join-Path $OutputRoot "$pbipName.pbip") -Force
Copy-Item $sourceReport (Join-Path $OutputRoot "$pbipName.Report") -Recurse -Force
Copy-Item $sourceSemanticModel (Join-Path $OutputRoot "$pbipName.SemanticModel") -Recurse -Force
Copy-Item $dataRoot (Join-Path $OutputRoot "data") -Recurse -Force

$expressionsPath = Join-Path $OutputRoot "$pbipName.SemanticModel\definition\expressions.tmdl"
if (!(Test-Path $expressionsPath -PathType Leaf)) { throw "Desktop artifact is missing expressions.tmdl: $expressionsPath" }

$text = [IO.File]::ReadAllText($expressionsPath, [Text.UTF8Encoding]::new($false))
$pattern = '(?m)(^\s*expression\s+DataFolder\s*=\s*")([^"]*)("\s+meta\b)'
$match = [regex]::Match($text, $pattern)
if (!$match.Success) { throw "Could not locate the DataFolder parameter expression in '$expressionsPath'." }

$patched = [regex]::Replace($text, $pattern, {
    param($m)
    $m.Groups[1].Value + $dataRoot + $m.Groups[3].Value
}, 1)
[IO.File]::WriteAllText($expressionsPath, $patched, [Text.UTF8Encoding]::new($false))

$verify = [IO.File]::ReadAllText($expressionsPath, [Text.UTF8Encoding]::new($false))
$verifyMatch = [regex]::Match($verify, $pattern)
if (!$verifyMatch.Success -or $verifyMatch.Groups[2].Value -ne $dataRoot) {
    throw "Desktop DataFolder parameter verification failed. Actual='$($verifyMatch.Groups[2].Value)' Expected='$dataRoot'."
}

Write-Host "DESKTOP-PARAMETER|DataFolder=$dataRoot"
Write-Host "DESKTOP-PARAMETER|Source=$BuildRoot"
Write-Host "DESKTOP-PARAMETER|Output=$OutputRoot"
Write-Host "DESKTOP-PARAMETER|PortableArtifactUnchanged=True"
Write-Host "DESKTOP-PARAMETER|ManageParametersRequired=False"

$desktopProject = Join-Path $OutputRoot "$pbipName.pbip"
if ($Open) {
    $desktop = Get-ChildItem @(
        "$env:ProgramFiles\Microsoft Power BI Desktop\bin\PBIDesktop.exe",
        "$env:ProgramFiles\WindowsApps\Microsoft.MicrosoftPowerBIDesktop_*\bin\PBIDesktop.exe",
        "$env:LOCALAPPDATA\Microsoft\Power BI Desktop\bin\PBIDesktop.exe"
    ) -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (!$desktop) { throw "Power BI Desktop executable was not found. The prepared PBIP is ready at '$desktopProject'." }
    Start-Process -FilePath $desktop.FullName -ArgumentList @($desktopProject)
    Write-Host "DESKTOP-OPEN|Project=$desktopProject"
}
else {
    Write-Host "DESKTOP-OPEN|Project=$desktopProject"
}

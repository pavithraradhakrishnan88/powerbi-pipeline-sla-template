[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$BuildRoot = "",
    [string]$ArtifactRoot = "",
    [string]$DesktopValidationRoot = ""
)

$ErrorActionPreference = "Stop"
$pbipName = "Pipeline_SLA_Tracker"
if ([string]::IsNullOrWhiteSpace($BuildRoot)) { $BuildRoot = Join-Path $RepoRoot "BuildResult\PBIP" }
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) { $ArtifactRoot = Join-Path $RepoRoot "artifacts" }
if ([string]::IsNullOrWhiteSpace($DesktopValidationRoot)) { $DesktopValidationRoot = Join-Path $RepoRoot "DesktopValidation\PBIP" }

$reportTemplate = Join-Path $RepoRoot "pbip\$pbipName.Report\.platform"
$semanticTemplate = Join-Path $RepoRoot "pbip\$pbipName.SemanticModel\.platform"
$generatedReport = Join-Path $BuildRoot "$pbipName.Report"
$generatedSemantic = Join-Path $BuildRoot "$pbipName.SemanticModel"
$artifactReport = Join-Path $ArtifactRoot "$pbipName.Report"
$artifactSemantic = Join-Path $ArtifactRoot "$pbipName.SemanticModel"

foreach ($path in @($reportTemplate,$semanticTemplate,$generatedReport,$generatedSemantic,$artifactReport,$artifactSemantic)) {
    if (!(Test-Path $path -PathType Leaf) -and !(Test-Path $path -PathType Container)) { throw "Desktop artifact preparation: required path is missing: $path" }
}

# Preserve the authoritative Fabric/Git project metadata byte-for-byte.
Copy-Item $reportTemplate (Join-Path $generatedReport ".platform") -Force
Copy-Item $semanticTemplate (Join-Path $generatedSemantic ".platform") -Force
Copy-Item $reportTemplate (Join-Path $artifactReport ".platform") -Force
Copy-Item $semanticTemplate (Join-Path $artifactSemantic ".platform") -Force

# Create a disposable, local Desktop-validation tree from the packaged artifact.
# IMPORTANT: this step only prepares the copy. Data materialization is deliberately
# performed by the next workflow step, after preparation has completed.
if (Test-Path $DesktopValidationRoot) { Remove-Item $DesktopValidationRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $DesktopValidationRoot | Out-Null
Copy-Item (Join-Path $ArtifactRoot '*') $DesktopValidationRoot -Recurse -Force

$pbip = Join-Path $DesktopValidationRoot "$pbipName.pbip"
if (!(Test-Path $pbip -PathType Leaf)) { throw "Desktop validation artifact is missing '$pbip'." }

foreach ($platform in @(
    (Join-Path $DesktopValidationRoot "$pbipName.Report\.platform"),
    (Join-Path $DesktopValidationRoot "$pbipName.SemanticModel\.platform")
)) {
    if (!(Test-Path $platform -PathType Leaf)) { throw "Desktop validation artifact is missing '$platform'." }
}

Write-Host "DESKTOP-PREPARE-GATE|PASS|PlatformFiles=2|Materialization=DEFERRED|PBIP=$pbip" -ForegroundColor Green

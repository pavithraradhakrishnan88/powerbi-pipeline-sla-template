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

$reportPlatform = Join-Path $RepoRoot "pbip\Pipeline_SLA_Tracker.Report\.platform"
$semanticPlatform = Join-Path $RepoRoot "pbip\Pipeline_SLA_Tracker.SemanticModel\.platform"
$generatedReport = Join-Path $BuildRoot "$pbipName.Report"
$generatedSemantic = Join-Path $BuildRoot "$pbipName.SemanticModel"
$artifactReport = Join-Path $ArtifactRoot "$pbipName.Report"
$artifactSemantic = Join-Path $ArtifactRoot "$pbipName.SemanticModel"

foreach ($path in @($reportPlatform,$semanticPlatform,$generatedReport,$generatedSemantic)) {
    if (!(Test-Path $path -PathType Leaf) -and !(Test-Path $path -PathType Container)) { throw "Desktop artifact preparation: required path is missing: $path" }
}

# Preserve the authoritative Fabric/Git project metadata byte-for-byte in the generated PBIP tree.
Copy-Item $reportPlatform (Join-Path $generatedReport ".platform") -Force
Copy-Item $semanticPlatform (Join-Path $generatedSemantic ".platform") -Force

# Explicitly materialize the two platform files into the packaged artifact.
# PBIP-Build uploads artifacts/**, so these are the exact files that must be present there.
New-Item -ItemType Directory -Force -Path $artifactReport | Out-Null
New-Item -ItemType Directory -Force -Path $artifactSemantic | Out-Null

Copy-Item $reportPlatform `
    (Join-Path $artifactReport ".platform") `
    -Force

Copy-Item $semanticPlatform `
    (Join-Path $artifactSemantic ".platform") `
    -Force

foreach ($platform in @(
    (Join-Path $artifactReport ".platform"),
    (Join-Path $artifactSemantic ".platform")
)) {
    if (!(Test-Path $platform -PathType Leaf)) {
        throw "Artifact preparation failed: missing $platform"
    }
}

# Create a disposable local Desktop-validation tree from the packaged artifact.
# IMPORTANT: this step only copies the portable artifact. Data materialization is
# deliberately a separate step so table loading precedes visual/UAT validation.
if (Test-Path $DesktopValidationRoot) { Remove-Item $DesktopValidationRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $DesktopValidationRoot | Out-Null
Copy-Item (Join-Path $ArtifactRoot '*') $DesktopValidationRoot -Recurse -Force

$pbip = Join-Path $DesktopValidationRoot "$pbipName.pbip"
if (!(Test-Path $pbip -PathType Leaf)) { throw "Desktop validation artifact is missing '$pbip'." }

Write-Host "DESKTOP-ARTIFACT-PREP|PASS|PlatformFiles=2|PortableCopy=PASS|PBIP=$pbip"
Write-Host "DESKTOP-ARTIFACT-PREP|NextStep=DATA-MATERIALIZATION"

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
$generatedDefinition = Join-Path $generatedReport "definition.pbir"
$artifactReport = Join-Path $ArtifactRoot "$pbipName.Report"
$artifactSemantic = Join-Path $ArtifactRoot "$pbipName.SemanticModel"
$artifactDefinition = Join-Path $artifactReport "definition.pbir"

foreach ($path in @($reportPlatform,$semanticPlatform,$generatedReport,$generatedSemantic)) {
    if (!(Test-Path $path -PathType Leaf) -and !(Test-Path $path -PathType Container)) { throw "Desktop artifact preparation: required path is missing: $path" }
}

# definition.pbir is a required Power BI Report artifact. Preserve it explicitly
# through the generated -> packaged -> DesktopValidation boundaries rather than
# relying only on recursive directory copying.
if (!(Test-Path $generatedDefinition -PathType Leaf)) {
    throw "Desktop artifact preparation failed: generated report definition.pbir is missing: $generatedDefinition"
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

# Explicit required-artifact preservation. This also makes the package boundary
# deterministic if a prior copy operation omitted or replaced the report definition.
Copy-Item $generatedDefinition $artifactDefinition -Force
if (!(Test-Path $artifactDefinition -PathType Leaf)) {
    throw "Desktop artifact preparation failed: packaged report definition.pbir is missing: $artifactDefinition"
}

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
Get-ChildItem $ArtifactRoot -Force | Copy-Item -Destination $DesktopValidationRoot -Recurse -Force

$pbip = Join-Path $DesktopValidationRoot "$pbipName.pbip"
if (!(Test-Path $pbip -PathType Leaf)) { throw "Desktop validation artifact is missing '$pbip'." }

$desktopDefinition = Join-Path $DesktopValidationRoot "$pbipName.Report\definition.pbir"
if (!(Test-Path $desktopDefinition -PathType Leaf)) {
    throw "Desktop artifact preparation failed: DesktopValidation report definition.pbir is missing: $desktopDefinition"
}

Write-Host "DESKTOP-REPORT-DEFINITION-GATE|PASS|definition.pbir preserved through generated->packaged->DesktopValidation"
Write-Host "DESKTOP-ARTIFACT-PREP|PASS|PlatformFiles=2|ReportDefinition=PASS|PortableCopy=PASS|PBIP=$pbip"
Write-Host "DESKTOP-ARTIFACT-PREP|NextStep=DATA-MATERIALIZATION"

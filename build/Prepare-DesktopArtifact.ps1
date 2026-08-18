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
# The portable artifact remains placeholder-based; only this copy is materialized.
if (Test-Path $DesktopValidationRoot) { Remove-Item $DesktopValidationRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $DesktopValidationRoot | Out-Null
Copy-Item (Join-Path $ArtifactRoot '*') $DesktopValidationRoot -Recurse -Force

& (Join-Path $PSScriptRoot "Materialize-PbipArtifact.ps1") -ArtifactRoot $DesktopValidationRoot
if ($LASTEXITCODE -ne 0) { throw "Desktop artifact materialization failed with exit code $LASTEXITCODE." }

$semanticRoot = Join-Path $DesktopValidationRoot "$pbipName.SemanticModel"
$dataRoot = Join-Path $DesktopValidationRoot "data"
$factTmdl = Join-Path $semanticRoot "definition\tables\Fact_Pipeline_SampleData.tmdl"
$expectedFact = [IO.Path]::GetFullPath((Join-Path $dataRoot "Fact_Pipeline_SampleData.csv"))

if (!(Test-Path $factTmdl -PathType Leaf)) { throw "Desktop validation artifact is missing '$factTmdl'." }
if (!(Test-Path $expectedFact -PathType Leaf)) { throw "Desktop validation artifact is missing '$expectedFact'." }
$factText = Get-Content -Raw $factTmdl
if ($factText -notmatch [regex]::Escape($expectedFact)) { throw "Desktop validation artifact Fact partition does not resolve to '$expectedFact'." }
if ($factText -match 'C:\\__PBIP_ARTIFACT_ROOT__|_work\\|/home/runner/|/opt/hostedtoolcache/') { throw "Desktop validation artifact still contains a placeholder or runner-specific path." }

foreach ($platform in @(
    (Join-Path $DesktopValidationRoot "$pbipName.Report\.platform"),
    (Join-Path $DesktopValidationRoot "$pbipName.SemanticModel\.platform")
)) {
    if (!(Test-Path $platform -PathType Leaf)) { throw "Desktop validation artifact is missing '$platform'." }
}

$pbip = Join-Path $DesktopValidationRoot "$pbipName.pbip"
if (!(Test-Path $pbip -PathType Leaf)) { throw "Desktop validation artifact is missing '$pbip'." }

Write-Host "DESKTOP-ARTIFACT-GATE|PASS|PlatformFiles=2|Materialized=PASS|FactSource=$expectedFact|PBIP=$pbip"
Write-Host "DESKTOP-ARTIFACT-GATE|DesktopOpenRequired=TRUE|DesktopErrorCaptureRequired=TRUE"

# =====================================
# build.ps1
#
# Responsibilities
# - Run validation
# - Update semantic model
# - Run Tabular Editor
# - Run pbi-tools
# - Create build artifacts
# =====================================

Write-Host "Starting build..."

# Run validation
& "$PSScriptRoot\validate.ps1"

# Create artifacts folder
$artifactPath = Join-Path $PSScriptRoot "..\artifacts"

if (!(Test-Path $artifactPath)) {
    New-Item -ItemType Directory -Path $artifactPath | Out-Null
    Write-Host "Artifacts folder created."
}

Write-Host "Build complete."
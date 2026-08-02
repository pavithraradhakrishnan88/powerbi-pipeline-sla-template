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

$sourceFiles = @(
    "src/Core/Models/*.cs",
    "src/Core/Serialization/*.cs",
    "src/Core/SchemaReader.cs",
    "src/Core/ModelBuilder.cs"
)

Write-Host "Build inputs:"
$sourceFiles | ForEach-Object { Write-Host " - $_" }

Write-Host "Generating metadata..."

$projectPath = Join-Path $PSScriptRoot "..\src\PowerBiPipelineSlaTemplate.Core"
$outputPath = Join-Path $PSScriptRoot "..\metadata\metadata.json"

if (Test-Path $projectPath) {
    & dotnet run --project $projectPath --extract-metadata --output $outputPath
} else {
    Write-Host "Skipping metadata generation: project not found at $projectPath"
}

Write-Host "Build complete."
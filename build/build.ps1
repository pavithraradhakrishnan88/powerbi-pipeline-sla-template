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

if (Test-Path $artifactPath) {
    Remove-Item -Path $artifactPath -Recurse -Force
}

New-Item -ItemType Directory -Path $artifactPath | Out-Null
Write-Host "Artifacts folder created."

$releaseEntries = @(
    "pbip",
    "docs",
    "data",
    "scripts",
    "theme",
    "LICENSE",
    "CHANGELOG.md",
    "README.md"
)

foreach ($entry in $releaseEntries) {
    $sourcePath = Join-Path $PSScriptRoot "..\$entry"
    $destinationPath = Join-Path $artifactPath (Split-Path $entry -Leaf)

    if (Test-Path $sourcePath) {
        if (Test-Path $destinationPath) {
            Remove-Item -Path $destinationPath -Recurse -Force
        }

        Copy-Item -Path $sourcePath -Destination $destinationPath -Recurse -Force
        Write-Host "Included release entry: $entry"
    }
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

Write-Host "Publishing PBIP artifacts to BuildResult..."

$pbipSourceRoot = Join-Path $PSScriptRoot "..\pbip"
$pbipOutputRoot = Join-Path $PSScriptRoot "..\BuildResult\PBIP"
$pbipName = "Pipeline_SLA_Tracker"

if (!(Test-Path $pbipOutputRoot)) {
    New-Item -ItemType Directory -Path $pbipOutputRoot | Out-Null
}

$pbipSourceFile = Join-Path $pbipSourceRoot "$pbipName.pbip"
$pbipSourceReport = Join-Path $pbipSourceRoot "$pbipName.Report"
$pbipSourceSemanticModel = Join-Path $pbipSourceRoot "$pbipName.SemanticModel"

if (Test-Path $pbipSourceFile) {
    cmd /c copy /Y "$pbipSourceFile" "$pbipOutputRoot\" | Out-Null
} else {
    Write-Host "Missing PBIP file: $pbipSourceFile"
}

if (Test-Path $pbipSourceReport) {
    robocopy $pbipSourceReport (Join-Path $pbipOutputRoot "$pbipName.Report") /MIR /NFL /NDL /NJH /NJS /NC /NS | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "robocopy failed for report folder with exit code $LASTEXITCODE"
    }
} else {
    Write-Host "Missing report folder: $pbipSourceReport"
}

if (Test-Path $pbipSourceSemanticModel) {
    robocopy $pbipSourceSemanticModel (Join-Path $pbipOutputRoot "$pbipName.SemanticModel") /MIR /NFL /NDL /NJH /NJS /NC /NS | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "robocopy failed for semantic model folder with exit code $LASTEXITCODE"
    }
} else {
    Write-Host "Missing semantic model folder: $pbipSourceSemanticModel"
}

$global:LASTEXITCODE = 0
Write-Host "Build complete."
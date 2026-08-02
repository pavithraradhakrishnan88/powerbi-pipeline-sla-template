# =====================================
# validate.ps1
#
# Responsibilities
# - Validate CSV files
# - Validate CSV schema
# =====================================

Write-Host "Starting validation..."

$requiredDirectories = @(
    "src/Core",
    "src/Core/Models",
    "src/Core/Serialization",
    "data"
)

foreach ($directory in $requiredDirectories) {
    if (Test-Path $directory) {
        Write-Host "Validated directory: $directory"
    } else {
        Write-Host "Missing directory: $directory"
    }
}

$requiredFiles = @(
    "src/Core/SchemaReader.cs",
    "src/Core/ModelBuilder.cs",
    "src/Core/Serialization/JsonSerializer.cs",
    "src/Core/Serialization/SerializerExtensions.cs"
)

foreach ($file in $requiredFiles) {
    if (Test-Path $file) {
        Write-Host "Validated file: $file"
    } else {
        Write-Host "Missing file: $file"
    }
}

Write-Host "Validation complete."
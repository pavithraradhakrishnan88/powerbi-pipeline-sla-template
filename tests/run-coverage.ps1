param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$resultsRoot = Join-Path $repoRoot "TestResults\Coverage"
if (Test-Path $resultsRoot) {
    Remove-Item $resultsRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resultsRoot | Out-Null

dotnet tool restore

dotnet test "$repoRoot\tests\PowerBiPipelineSlaTemplate.Tests\PowerBiPipelineSlaTemplate.Tests.csproj" --configuration $Configuration --settings "$repoRoot\tests\coverage.runsettings" --results-directory "$repoRoot\TestResults"
dotnet test "$repoRoot\tests\PowerBiPipelineSlaTemplate.IntegrationTests\PowerBiPipelineSlaTemplate.IntegrationTests.csproj" --configuration $Configuration --settings "$repoRoot\tests\coverage.runsettings" --results-directory "$repoRoot\TestResults"

$coverageFiles = Get-ChildItem -Path "$repoRoot\TestResults" -Filter "coverage.cobertura.xml" -Recurse | Select-Object -ExpandProperty FullName
if (-not $coverageFiles) {
    throw "No cobertura files were produced."
}

$reportDir = Join-Path $resultsRoot "Html"
$joinedReports = [string]::Join(";", $coverageFiles)
dotnet tool run reportgenerator -- "-reports:$joinedReports" "-targetdir:$reportDir" "-reporttypes:Html;Cobertura;OpenCover"

Write-Output "Coverage reports generated under $resultsRoot"

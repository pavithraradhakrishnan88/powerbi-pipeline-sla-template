using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;
using Xunit;

namespace PowerBiPipelineSlaTemplate.IntegrationTests;

public class PipelineIntegrationTests
{
    [Fact]
    public void Run_ShouldExecuteFullPipeline_AndGenerateExpectedStructure()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var orchestrator = new PipelineOrchestrator();

        var result = orchestrator.Run(fixture.Options);

        result.Should().NotBeNull();
        Directory.Exists(fixture.SemanticModelRootPath).Should().BeTrue();
        Directory.Exists(fixture.ReportRootPath).Should().BeTrue();
        File.Exists(fixture.MetadataOutputPath).Should().BeTrue();

        File.Exists(Path.Combine(fixture.SemanticModelRootPath, "definition.pbism")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.SemanticModelRootPath, "definition", "model.tmdl")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.SemanticModelRootPath, "definition", "database.tmdl")).Should().BeTrue();

        File.Exists(Path.Combine(fixture.ReportRootPath, "definition.pbir")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.ReportRootPath, "definition", "report.json")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.ReportRootPath, "definition", "pages", "pages.json")).Should().BeTrue();

        ValidateJsonFiles(fixture.ReportRootPath);
    }

    [Fact]
    public void Run_ShouldSucceed_ForLargeDataset()
    {
        using var workspace = new TemporaryWorkspace();
        var dataDir = workspace.CreateDirectory("data");
        SampleCsvGenerator.GenerateLargeDataset(dataDir, 1000);

        var pbipRoot = workspace.CreateDirectory("pbip");
        var repoRoot = WorkspacePaths.FindRepoRoot();
        var templateSource = Path.Combine(repoRoot, "pbip", "Pipeline_SLA_Tracker.Report");
        var templateTarget = Path.Combine(pbipRoot, "Pipeline_SLA_Tracker.Report");
        CopyDirectory(templateSource, templateTarget);

        var options = new PipelineOptions
        {
            DataDirectoryPath = dataDir,
            SemanticModelRootPath = Path.Combine(pbipRoot, "Pipeline SLA.SemanticModel"),
            ReportRootPath = Path.Combine(pbipRoot, "Pipeline SLA.Report"),
            ReportTemplateRootPath = templateTarget,
            SemanticModelRelativePath = "../Pipeline SLA.SemanticModel",
            MetadataOutputPath = workspace.GetPath(Path.Combine("metadata", "metadata.json")),
            ThrowOnValidationError = true
        };

        var orchestrator = new PipelineOrchestrator();

        var result = orchestrator.Run(options);

        result.Model.Tables.Should().NotBeEmpty();
        File.Exists(Path.Combine(options.SemanticModelRootPath, "definition", "tables", "Fact_Pipeline_SampleData.tmdl")).Should().BeTrue();
    }

    [Fact]
    public void Run_ShouldMatchGoldenSnapshot_ForDeterministicFiles()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var orchestrator = new PipelineOrchestrator();
        _ = orchestrator.Run(fixture.Options);

        var repoRoot = WorkspacePaths.FindRepoRoot();
        var expectedRoot = Path.Combine(repoRoot, "tests", "GoldenFiles", "PipelineSnapshot");

        var actualSnapshotRoot = Path.Combine(fixture.ReportRootPath, "..", "SnapshotActual");
        Directory.CreateDirectory(actualSnapshotRoot);

        CopySnapshotFile(Path.Combine(fixture.SemanticModelRootPath, "definition", "model.tmdl"), Path.Combine(actualSnapshotRoot, "SemanticModel", "definition", "model.tmdl"));
        CopySnapshotFile(Path.Combine(fixture.SemanticModelRootPath, "definition", "relationships.tmdl"), Path.Combine(actualSnapshotRoot, "SemanticModel", "definition", "relationships.tmdl"));
        CopySnapshotFile(Path.Combine(fixture.ReportRootPath, "definition.pbir"), Path.Combine(actualSnapshotRoot, "Report", "definition.pbir"));

        GoldenFileComparer.AssertDirectoryMatches(
            expectedRoot,
            actualSnapshotRoot,
            Path.Combine("SemanticModel", "definition", "model.tmdl"),
            Path.Combine("SemanticModel", "definition", "relationships.tmdl"),
            Path.Combine("Report", "definition.pbir"));
    }

    private static void ValidateJsonFiles(string rootPath)
    {
        var jsonFiles = Directory.GetFiles(rootPath, "*.json", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(rootPath, "*.pbir", SearchOption.AllDirectories));

        foreach (var file in jsonFiles)
        {
            var content = File.ReadAllText(file);
            Action act = () => JsonDocument.Parse(content);
            act.Should().NotThrow($"File should contain valid JSON: {file}");
        }
    }

    private static void CopySnapshotFile(string source, string destination)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(source, destination, overwrite: true);
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var sourceFile in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, sourceFile);
            var destination = Path.Combine(destinationPath, relative);
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(sourceFile, destination, overwrite: true);
        }
    }
}

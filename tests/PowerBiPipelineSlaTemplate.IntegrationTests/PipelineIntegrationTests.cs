using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

    [Fact]
    public void Run_ShouldGenerateImportPartitionsWithValidSourceNesting()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var orchestrator = new PipelineOrchestrator();
        _ = orchestrator.Run(fixture.Options);

        var tableFiles = Directory.GetFiles(
            Path.Combine(fixture.SemanticModelRootPath, "definition", "tables"),
            "*.tmdl",
            SearchOption.TopDirectoryOnly);

        foreach (var file in tableFiles)
        {
            AssertImportPartitionSourceNesting(file);
        }
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

    private static void AssertImportPartitionSourceNesting(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!Regex.IsMatch(lines[i], @"^[\t ]*partition\s+\S+\s*=\s*m\s*$"))
            {
                continue;
            }

            var sourceIndex = Array.FindIndex(lines, i + 1, line => Regex.IsMatch(line, @"^[\t ]*source\s*=\s*$"));
            sourceIndex.Should().BeGreaterThan(i, $"M partition in {filePath} must contain a multiline source block.");

            var partitionIndent = CountIndent(lines[i]);
            var sourceIndent = CountIndent(lines[sourceIndex]);
            sourceIndent.Should().Be(partitionIndent + 1, $"source must be nested one level under the partition in {filePath}");

            var letIndex = sourceIndex + 1;
            while (letIndex < lines.Length && string.IsNullOrWhiteSpace(lines[letIndex]))
            {
                letIndex++;
            }

            lines[letIndex].Trim().Should().Be("let", $"the source block in {filePath} must begin with let");
            CountIndent(lines[letIndex]).Should().Be(sourceIndent + 1, $"let must be nested under source in {filePath}");

            var inIndex = Array.FindIndex(lines, letIndex + 1, line => line.Trim() == "in");
            inIndex.Should().BeGreaterThan(letIndex, $"the let block in {filePath} must contain in");
            CountIndent(lines[inIndex]).Should().Be(sourceIndent + 1, $"in must be nested under source in {filePath}");

            for (var lineIndex = letIndex + 1; lineIndex < inIndex; lineIndex++)
            {
                if (string.IsNullOrWhiteSpace(lines[lineIndex]))
                {
                    continue;
                }

                CountIndent(lines[lineIndex]).Should().BeGreaterThan(sourceIndent + 1, $"M binding lines must be nested under let in {filePath}");
            }

            var resultIndex = inIndex + 1;
            while (resultIndex < lines.Length && string.IsNullOrWhiteSpace(lines[resultIndex]))
            {
                resultIndex++;
            }

            resultIndex.Should().BeLessThan(lines.Length, $"partition in {filePath} must include a result expression after in");
            CountIndent(lines[resultIndex]).Should().BeGreaterThan(sourceIndent + 1, $"the final M expression must be nested under in in {filePath}");
        }
    }

    private static int CountIndent(string line)
    {
        var count = 0;
        foreach (var ch in line)
        {
            if (ch == '\t')
            {
                count++;
                continue;
            }

            if (ch == ' ')
            {
                count++;
                continue;
            }

            break;
        }

        return count;
    }
}

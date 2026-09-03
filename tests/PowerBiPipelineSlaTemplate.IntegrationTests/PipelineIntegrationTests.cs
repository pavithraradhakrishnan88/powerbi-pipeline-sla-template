using System;
using System.Collections.Generic;
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

    [Fact]
    public void Run_ShouldResolveDateVariationDefaultHierarchies()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var orchestrator = new PipelineOrchestrator();
        _ = orchestrator.Run(fixture.Options);

        var tablesRoot = Path.Combine(fixture.SemanticModelRootPath, "definition", "tables");
        var factPath = Path.Combine(tablesRoot, "Fact_Pipeline_SampleData.tmdl");
        var factText = File.ReadAllText(factPath);
        var hierarchyTargets = GetHierarchyTargets(tablesRoot);

        var expectedColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ScheduledStart"] = "LocalDateTable_9043e032-67a4-45e7-bf88-28fec57966b9",
            ["ActualStart"] = "LocalDateTable_ac53fd01-924f-4452-a3f3-a0f9a1df973c",
            ["ScheduledEnd"] = "LocalDateTable_d4583ee4-86f0-47d3-96ae-303f0614bf0d",
            ["ActualEnd"] = "LocalDateTable_1c11b445-5c42-44a6-9f02-0bc10f99ee27"
        };

        foreach (var expected in expectedColumns)
        {
            var reference = GetDefaultHierarchyReference(factText, expected.Key);
            reference.Should().Be($"{expected.Value}.'Date Hierarchy'");

            var target = SplitObjectReference(reference);
            hierarchyTargets.Should().Contain(target, $"column {expected.Key} should resolve to an emitted hierarchy object");
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

    private static HashSet<(string Table, string Hierarchy)> GetHierarchyTargets(string tablesRoot)
    {
        var targets = new HashSet<(string Table, string Hierarchy)>();

        foreach (var file in Directory.GetFiles(tablesRoot, "*.tmdl", SearchOption.TopDirectoryOnly))
        {
            var lines = File.ReadAllLines(file);
            var tableLine = Array.Find(lines, line => Regex.IsMatch(line.Trim(), @"^table\s+"));
            tableLine.Should().NotBeNull($"table declaration should exist in {file}");

            var tableName = UnquoteIdentifier(Regex.Match(tableLine!, @"^table\s+(.+)$").Groups[1].Value);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!Regex.IsMatch(trimmed, @"^hierarchy\s+"))
                {
                    continue;
                }

                var hierarchyName = UnquoteIdentifier(Regex.Match(trimmed, @"^hierarchy\s+(.+)$").Groups[1].Value);
                targets.Add((tableName, hierarchyName));
            }
        }

        return targets;
    }

    private static string GetDefaultHierarchyReference(string factText, string columnName)
    {
        var pattern = $@"(?ms)^\tcolumn\s+{Regex.Escape(columnName)}\s*$.*?^\t\tvariation\s+Variation\s*$.*?^\t\t\tdefaultHierarchy:\s*(?<reference>.+?)\s*$";
        var match = Regex.Match(factText, pattern);
        match.Success.Should().BeTrue($"column {columnName} should contain a variation defaultHierarchy");
        return match.Groups["reference"].Value;
    }

    private static (string Table, string Hierarchy) SplitObjectReference(string reference)
    {
        var inQuotes = false;
        for (var i = 0; i < reference.Length; i++)
        {
            var ch = reference[i];
            if (ch == '\'')
            {
                if (inQuotes && i + 1 < reference.Length && reference[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (ch == '.' && !inQuotes)
            {
                return (UnquoteIdentifier(reference[..i]), UnquoteIdentifier(reference[(i + 1)..]));
            }
        }

        throw new InvalidOperationException($"Invalid TMDL object reference: {reference}");
    }

    private static string UnquoteIdentifier(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\''
            ? trimmed[1..^1].Replace("''", "'")
            : trimmed;
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

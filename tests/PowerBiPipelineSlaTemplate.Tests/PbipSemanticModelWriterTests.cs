using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class PbipSemanticModelWriterTests
{
    [Fact]
    public void WriteSemanticModel_ShouldGenerateSemanticModelArtifacts()
    {
        using var workspace = new TemporaryWorkspace();
        var pbipRoot = workspace.CreateDirectory("pbip");
        var semanticModelPath = Path.Combine(pbipRoot, "Pipeline SLA.SemanticModel");

        var writer = new PbipSemanticModelWriter();
        var model = SampleModel.Normal();

        writer.WriteSemanticModel(model, semanticModelPath);

        File.Exists(Path.Combine(semanticModelPath, "definition.pbism")).Should().BeTrue();
        File.Exists(Path.Combine(semanticModelPath, "definition", "model.tmdl")).Should().BeTrue();
        File.Exists(Path.Combine(pbipRoot, "Pipeline SLA.pbip")).Should().BeTrue();
    }

    [Fact]
    public void Write_ShouldGenerateReport_WhenTemplateExists()
    {
        using var workspace = new TemporaryWorkspace();
        var pbipRoot = workspace.CreateDirectory("pbip");
        var semanticModelPath = Path.Combine(pbipRoot, "Pipeline SLA.SemanticModel");

        var repoRoot = WorkspacePaths.FindRepoRoot();
        var sourceTemplatePath = Path.Combine(repoRoot, "pbip", "Pipeline_SLA_Tracker.Report");
        var destinationTemplatePath = Path.Combine(pbipRoot, "Pipeline_SLA_Tracker.Report");
        Directory.CreateDirectory(destinationTemplatePath);

        foreach (var sourceFile in Directory.GetFiles(sourceTemplatePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceTemplatePath, sourceFile);
            var destination = Path.Combine(destinationTemplatePath, relative);
            var dir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.Copy(sourceFile, destination, overwrite: true);
        }

        var writer = new PbipSemanticModelWriter();

        writer.Write(SampleModel.Normal(), semanticModelPath);

        var reportRoot = Path.Combine(pbipRoot, "Pipeline SLA.Report");
        Directory.Exists(reportRoot).Should().BeTrue();
        File.Exists(Path.Combine(reportRoot, "definition.pbir")).Should().BeTrue();

        var visualFiles = Directory.GetFiles(
            Path.Combine(reportRoot, "definition", "pages"),
            "visual.json",
            SearchOption.AllDirectories);

        visualFiles.Should().NotBeEmpty();

        foreach (var visualFile in visualFiles)
        {
            var root = JsonNode.Parse(File.ReadAllText(visualFile));
            root.Should().NotBeNull();
            root.Should().BeOfType<JsonObject>();

            var visualContainerObjects = root!["visual"]?["visualContainerObjects"] as JsonObject
                ?? root["visualContainerObjects"] as JsonObject;

            if (visualContainerObjects?["title"] is JsonNode title)
            {
                title.Should().BeOfType<JsonArray>();
            }
        }

        var kpiVisual = Array.Find(
            visualFiles,
            file => file.EndsWith("9258a26561818de46b08\\visual.json", StringComparison.OrdinalIgnoreCase));

        kpiVisual.Should().NotBeNull();
        var kpiRoot = JsonNode.Parse(File.ReadAllText(kpiVisual!));
        kpiRoot.Should().NotBeNull();
        var kpiTitle = kpiRoot!["visual"]?["visualContainerObjects"]?["title"] as JsonArray
            ?? kpiRoot["visualContainerObjects"]?["title"] as JsonArray;

        kpiTitle.Should().NotBeNull();
        kpiTitle!.Count.Should().Be(1);
        kpiTitle[0]?["properties"]?["text"]?["expr"]?["Literal"]?["Value"]?.GetValue<string>()
            .Should().Be("'Pipeline SLA Tracker'");
    }

    [Fact]
    public void WriteSemanticModel_ShouldSkipIdAndKeyColumnsFromGenericSumMeasures()
    {
        using var workspace = new TemporaryWorkspace();
        var pbipRoot = workspace.CreateDirectory("pbip");
        var semanticModelPath = Path.Combine(pbipRoot, "Pipeline_SLA_Tracker.SemanticModel");

        var model = new ModelBuildResult
        {
            Tables = new List<BuiltTable>
            {
                new BuiltTable
                {
                    Name = "Fact_Pipeline_SampleData",
                    IsFactTable = true,
                    Columns = new List<BuiltColumn>
                    {
                        new BuiltColumn { Name = "PipelineID", DataType = "Int64", IsPrimaryKey = true },
                        new BuiltColumn { Name = "DurationHours", DataType = "Decimal" },
                        new BuiltColumn { Name = "SLAHours", DataType = "Decimal" },
                        new BuiltColumn { Name = "RetryCount", DataType = "Int64" }
                    }
                }
            }
        };

        var writer = new PbipSemanticModelWriter();
        writer.WriteSemanticModel(model, semanticModelPath);

        var measuresPath = Path.Combine(semanticModelPath, "definition", "tables", "_Measures.tmdl");
        File.Exists(measuresPath).Should().BeTrue();

        var content = File.ReadAllText(measuresPath);
        content.Should().NotContain("SUM(Fact_Pipeline_SampleData[PipelineID])");
        content.Should().Contain("measure 'Total Runtime' = SUM(Fact_Pipeline_SampleData[DurationHours])");
        content.Should().Contain("measure 'Average Runtime' = AVERAGE(Fact_Pipeline_SampleData[DurationHours])");
        content.Should().Contain("measure 'Success Rate %' = DIVIDE([Successful Runs],[Active Pipelines])");
    }

    [Fact]
    public void WriteSemanticModel_ShouldThrow_ForInvalidInput()
    {
        var writer = new PbipSemanticModelWriter();

        Action act = () => writer.WriteSemanticModel(null!, "");

        act.Should().Throw<ArgumentNullException>();
    }
}

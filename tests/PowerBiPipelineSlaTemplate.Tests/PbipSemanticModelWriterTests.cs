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
    public void WriteSemanticModel_ShouldGenerateDataFolderParameterAndUseItForPartitions()
    {
        using var workspace = new TemporaryWorkspace();
        var pbipRoot = workspace.CreateDirectory("pbip");
        var semanticModelPath = Path.Combine(pbipRoot, "Pipeline SLA.SemanticModel");

        var writer = new PbipSemanticModelWriter();
        writer.WriteSemanticModel(SampleModel.Normal(), semanticModelPath);

        var expressions = File.ReadAllText(Path.Combine(semanticModelPath, "definition", "expressions.tmdl"));
        expressions.Should().Contain("expression DataFolder =");
        expressions.Should().Contain("IsParameterQuery=true");
        expressions.Should().Contain("IsParameterQueryRequired=true");

        foreach (var table in new[] { "Fact_Pipeline_SampleData", "Dim_Category" })
        {
            var tmdl = File.ReadAllText(Path.Combine(semanticModelPath, "definition", "tables", $"{table}.tmdl"));
            tmdl.Should().Contain($"File.Contents(DataFolder & \"\\{table}.csv\")");
            tmdl.Should().NotContain("C:\\Users\\");
        }
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
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.Copy(sourceFile, destination, overwrite: true);
        }

        var writer = new PbipSemanticModelWriter();
        writer.Write(SampleModel.Normal(), semanticModelPath);

        var reportRoot = Path.Combine(pbipRoot, "Pipeline SLA.Report");
        Directory.Exists(reportRoot).Should().BeTrue();
        File.Exists(Path.Combine(reportRoot, "definition.pbir")).Should().BeTrue();

        var visualFiles = Directory.GetFiles(Path.Combine(reportRoot, "definition", "pages"), "visual.json", SearchOption.AllDirectories);
        visualFiles.Should().NotBeEmpty();

        foreach (var visualFile in visualFiles)
        {
            var root = JsonNode.Parse(File.ReadAllText(visualFile));
            root.Should().NotBeNull();
            root.Should().BeOfType<JsonObject>();

            var visual = root!["visual"] as JsonObject;
            visual.Should().NotBeNull();
            visual!["visualContainerObjects"]?.Should().BeOfType<JsonObject>();

            if (visual["visualContainerObjects"]?["title"] is JsonNode title)
                title.Should().BeOfType<JsonArray>();

            root["visualContainerObjects"].Should().BeNull();
        }

        var kpiVisual = Array.Find(visualFiles, file => file.EndsWith("d3f7987300602011509c\\visual.json", StringComparison.OrdinalIgnoreCase));
        kpiVisual.Should().NotBeNull();

        var kpiRoot = JsonNode.Parse(File.ReadAllText(kpiVisual!));
        kpiRoot.Should().NotBeNull();
        var kpiVisualNode = kpiRoot!["visual"] as JsonObject;
        kpiVisualNode.Should().NotBeNull();
        var kpiTitle = kpiVisualNode!["visualContainerObjects"]?["title"] as JsonArray;

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

        var tablesPath = Path.Combine(semanticModelPath, "definition", "tables");
        var measuresPath = Path.Combine(tablesPath, "_Measure Table.tmdl");
        var legacyMeasuresPath = Path.Combine(tablesPath, "_Measures.tmdl");

        File.Exists(measuresPath).Should().BeTrue();
        File.Exists(legacyMeasuresPath).Should().BeFalse();

        var content = File.ReadAllText(measuresPath);
        content.Should().Contain("table '_Measure Table'");
        content.Should().NotContain("SUM(Fact_Pipeline_SampleData[PipelineID])");
        content.Should().Contain("measure 'Total Runtime' = SUM(Fact_Pipeline_SampleData[DurationHours])");
        content.Should().Contain("measure 'Average Runtime' = AVERAGE(Fact_Pipeline_SampleData[DurationHours])");
        content.Should().Contain("measure 'Success Rate %' = DIVIDE([Successful Runs],[Active Pipelines])");

        var modelTmdl = File.ReadAllText(Path.Combine(semanticModelPath, "definition", "model.tmdl"));
        modelTmdl.Should().Contain("ref table '_Measure Table'");
        modelTmdl.Should().NotContain("ref table _Measures");
    }

    [Fact]
    public void WriteSemanticModel_ShouldThrow_ForInvalidInput()
    {
        var writer = new PbipSemanticModelWriter();
        Action act = () => writer.WriteSemanticModel(null!, "");
        act.Should().Throw<ArgumentNullException>();
    }
}

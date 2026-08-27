using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core;
using PowerBiPipelineSlaTemplate.Core.Models;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class MeasureBuilderTests
{
    [Fact]
    public void WriteTmdlArtifacts_ShouldGenerateMeasureFiles_ForFactNumericColumns()
    {
        using var workspace = new TemporaryWorkspace();
        var modelRoot = workspace.CreateDirectory("model");
        var builder = new ModelBuilder();
        var model = builder.Build(SampleMetadata.Normal());

        builder.WriteTmdlArtifacts(model, modelRoot);

        var kpiPath = Path.Combine(modelRoot, "Measures", "KPIs.tmdl");
        File.Exists(kpiPath).Should().BeTrue();
        File.ReadAllText(kpiPath).Should().Contain("No inferred numeric columns available for this group.");
    }

    [Fact]
    public void WriteTmdlArtifacts_ShouldHandleInvalidMeasures_WhenNoNumericColumns()
    {
        using var workspace = new TemporaryWorkspace();
        var modelRoot = workspace.CreateDirectory("model");
        var schema = new DatabaseSchema
        {
            Tables =
            {
                new TableDefinition
                {
                    Name = "Fact_TextOnly",
                    IsFactTable = true,
                    Columns =
                    {
                        new ColumnDefinition { Name = "Id", Type = "string" },
                        new ColumnDefinition { Name = "Status", Type = "string" }
                    }
                }
            }
        };

        var builder = new ModelBuilder();
        var model = builder.Build(schema);

        builder.WriteTmdlArtifacts(model, modelRoot);

        var durationPath = Path.Combine(modelRoot, "Measures", "Duration.tmdl");
        File.Exists(durationPath).Should().BeTrue();
        File.ReadAllText(durationPath).Should().Contain("No inferred numeric columns");
    }

    [Fact]
    public void MetadataDefinitions_ShouldContainExpectedMeasuresAndConsistentFloatingBarDefinitions()
    {
        var repoRoot = WorkspacePaths.FindRepoRoot();
        var metadataPath = Path.Combine(repoRoot, "scripts", "metadata", "MeasureDefinitions.json");
        var json = File.ReadAllText(metadataPath);
        using var document = JsonDocument.Parse(json);
        var measures = document.RootElement.GetProperty("measures").EnumerateArray().ToList();

        measures.Should().ContainSingle(m => m.GetProperty("MeasureID").GetString() == "M008" && m.GetProperty("Name").GetString() == "SLA Breach %" && m.GetProperty("Expression").GetString()!.Contains("SLAStatus") && m.GetProperty("DataType").GetString() == "Percentage");
        measures.Should().ContainSingle(m => m.GetProperty("MeasureID").GetString() == "M016" && m.GetProperty("Name").GetString() == "Timeline Base" && m.GetProperty("Format").GetString() == "0" && m.GetProperty("DataType").GetString() == "Whole Number");
        measures.Should().ContainSingle(m => m.GetProperty("MeasureID").GetString() == "M017" && m.GetProperty("Name").GetString() == "Floating Bar Duration" && m.GetProperty("Expression").GetString() == "[Average Runtime]" && m.GetProperty("Format").GetString() == "#,##0.00");
    }

    [Fact]
    public void PbipWriter_ShouldEmitMetadataDrivenMeasuresIntoCanonicalMeasureTable()
    {
        using var workspace = new TemporaryWorkspace();
        var pbipRoot = workspace.CreateDirectory("pbip");
        var semanticModelPath = Path.Combine(pbipRoot, "Pipeline_SLA_Tracker.SemanticModel");
        var writer = new PbipSemanticModelWriter();
        var model = SampleModel.Normal();

        writer.WriteSemanticModel(model, semanticModelPath);

        var tablesPath = Path.Combine(semanticModelPath, "definition", "tables");
        var measuresPath = Path.Combine(tablesPath, "_Measure Table.tmdl");
        var legacyMeasuresPath = Path.Combine(tablesPath, "_Measures.tmdl");

        File.Exists(measuresPath).Should().BeTrue();
        File.Exists(legacyMeasuresPath).Should().BeFalse();

        var content = File.ReadAllText(measuresPath);
        content.Should().Contain("table '_Measure Table'");
        content.Should().Contain("measure 'SLA Breach %'");
        content.Should().Contain("measure 'Timeline Base'");
        content.Should().Contain("measure 'Floating Bar Duration'");
        content.Should().Contain("measure 'Average Runtime'");

        var modelTmdl = File.ReadAllText(Path.Combine(semanticModelPath, "definition", "model.tmdl"));
        modelTmdl.Should().Contain("ref table '_Measure Table'");
        modelTmdl.Should().NotContain("ref table _Measures");
    }
}

using System.IO;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core;
using PowerBiPipelineSlaTemplate.Core.Models;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
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
        File.ReadAllText(kpiPath).Should().Contain("SUM");
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
}

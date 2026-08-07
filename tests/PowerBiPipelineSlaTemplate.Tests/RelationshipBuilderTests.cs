using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core;
using PowerBiPipelineSlaTemplate.Core.Models;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class RelationshipBuilderTests
{
    [Fact]
    public void Build_ShouldCreateRelationship_ForResolvableForeignKey()
    {
        var builder = new ModelBuilder();

        var model = builder.Build(SampleMetadata.Normal());

        model.Relationships.Should().ContainSingle();
        model.Relationships[0].FromTable.Should().Be("Fact_Pipeline_SampleData");
        model.Relationships[0].ToTable.Should().Be("Dim_Category");
    }

    [Fact]
    public void Build_ShouldNotCreateRelationship_ForUnresolvableForeignKey()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            {
                new TableDefinition
                {
                    Name = "Fact_Pipeline",
                    IsFactTable = true,
                    Columns =
                    {
                        new ColumnDefinition { Name = "PipelineId", Type = "string", Nullable = false },
                        new ColumnDefinition { Name = "UnknownEntityId", Type = "int", IsForeignKeyCandidate = true, Nullable = false }
                    }
                },
                new TableDefinition
                {
                    Name = "Dim_Category",
                    IsDimensionTable = true,
                    Columns =
                    {
                        new ColumnDefinition { Name = "DescriptionText", Type = "string", Nullable = false }
                    }
                }
            }
        };

        var builder = new ModelBuilder();
        var model = builder.Build(schema);

        model.Relationships.Should().BeEmpty();
    }
}

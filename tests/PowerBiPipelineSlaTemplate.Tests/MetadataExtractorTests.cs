using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core;
using PowerBiPipelineSlaTemplate.Core.Models;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class MetadataExtractorTests
{
    [Fact]
    public async Task ExtractFromCsvAsync_ShouldReturnMetadata_ForValidCsv()
    {
        using var workspace = new TemporaryWorkspace();
        var filePath = Path.Combine(workspace.RootPath, "Fact_Test.csv");
        File.WriteAllText(filePath, "Id,Amount,IsActive\n1,10.5,true\n2,20.0,false\n");

        var extractor = new MetadataExtractor();

        var result = await extractor.ExtractFromCsvAsync(filePath, "Fact_Test");

        result.Tables.Should().HaveCount(1);
        result.Tables[0].Columns.Should().HaveCount(3);
        result.Tables[0].Columns.Should().Contain(column => column.Name == "Amount" && column.Type == "decimal");
    }

    [Fact]
    public void ExtractFromCsv_ShouldThrow_WhenFileIsMissing()
    {
        var extractor = new MetadataExtractor();
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.csv");

        Action act = () => extractor.ExtractFromCsv(missingPath);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void BuildMetadata_ShouldHandleEmptyInput()
    {
        var extractor = new MetadataExtractor();

        var metadata = extractor.BuildMetadata(Array.Empty<TableMetadata>());

        metadata.Tables.Should().BeEmpty();
    }

    [Fact]
    public void ToJson_ShouldSerializeMetadata()
    {
        var extractor = new MetadataExtractor();
        var metadata = extractor.BuildMetadata(new List<TableMetadata>
        {
            new()
            {
                Name = "TableA",
                Columns = new List<ColumnMetadata> { new() { Name = "Id", Type = "integer" } }
            }
        });

        var json = extractor.ToJson(metadata, indented: true);

        json.Should().Contain("TableA");
        json.Should().Contain("Id");
    }
}

using System;
using System.IO;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class SchemaReaderTests
{
    [Fact]
    public void Read_ShouldReturnSchema_ForNormalScenario()
    {
        using var workspace = new TemporaryWorkspace();
        var dataPath = workspace.CreateDirectory("data");
        SampleCsvGenerator.CopyScenarioToWorkspace("Normal", dataPath);

        var reader = new SchemaReader(dataPath, throwOnValidationError: true);

        var schema = reader.Read();

        schema.Tables.Should().HaveCount(2);
        schema.Tables.Should().Contain(table => table.Name == "Fact_Pipeline_SampleData");
        schema.Tables.Should().Contain(table => table.Name == "Dim_Category");
    }

    [Fact]
    public void Read_ShouldThrow_WhenDirectoryMissing()
    {
        var reader = new SchemaReader(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Action act = () => reader.Read();

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Read_ShouldThrow_ForDuplicateHeaders()
    {
        using var workspace = new TemporaryWorkspace();
        var dataPath = workspace.CreateDirectory("data");
        File.WriteAllText(Path.Combine(dataPath, "Fact_Dupe.csv"), "Id,Id\n1,1\n");

        var reader = new SchemaReader(dataPath, throwOnValidationError: true);

        Action act = () => reader.Read();

        act.Should().Throw<InvalidDataException>().WithMessage("*duplicate column names*");
    }

    [Fact]
    public void Read_ShouldThrow_WhenThrowOnValidationErrorIsTrue_ForInvalidSchemaRows()
    {
        using var workspace = new TemporaryWorkspace();
        var dataPath = workspace.CreateDirectory("data");
        SampleCsvGenerator.CopyScenarioToWorkspace("InvalidSchema", dataPath);

        var reader = new SchemaReader(dataPath, throwOnValidationError: true);

        Action act = () => reader.Read();

        act.Should().Throw<InvalidDataException>().WithMessage("*expected*");
    }

    [Fact]
    public void Read_ShouldInferStringTypes_ForInvalidDataTypesScenario()
    {
        using var workspace = new TemporaryWorkspace();
        var dataPath = workspace.CreateDirectory("data");
        SampleCsvGenerator.CopyScenarioToWorkspace("InvalidDataTypes", dataPath);

        var reader = new SchemaReader(dataPath);

        var schema = reader.Read();

        schema.Tables.Should().NotBeEmpty();
        var fact = schema.Tables.Find(table => table.Name == "Fact_Pipeline_SampleData");
        fact.Should().NotBeNull();
        fact!.Columns.Should().Contain(column => column.Name == "DurationHours" && column.Type == "string");
    }

    [Fact]
    public void Read_ShouldHandleLargeDataset()
    {
        using var workspace = new TemporaryWorkspace();
        var dataPath = workspace.CreateDirectory("data");
        SampleCsvGenerator.GenerateLargeDataset(dataPath, 1000);

        var reader = new SchemaReader(dataPath, throwOnValidationError: true);

        var schema = reader.Read();

        schema.Tables.Should().Contain(table => table.Name == "Fact_Pipeline_SampleData" && table.RowCount == 1000);
    }
}

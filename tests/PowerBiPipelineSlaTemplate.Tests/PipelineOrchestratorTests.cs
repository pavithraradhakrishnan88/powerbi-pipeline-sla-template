using System;
using System.IO;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class PipelineOrchestratorTests
{
    [Fact]
    public void Run_ShouldSucceed_ForNormalScenario()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var orchestrator = new PipelineOrchestrator();

        var result = orchestrator.Run(fixture.Options);

        result.Should().NotBeNull();
        result.Model.Tables.Should().NotBeEmpty();
        File.Exists(fixture.MetadataOutputPath).Should().BeTrue();
        Directory.Exists(fixture.SemanticModelRootPath).Should().BeTrue();
        Directory.Exists(fixture.ReportRootPath).Should().BeTrue();
    }

    [Fact]
    public void Run_ShouldPatchFactColumns_AndKeepMeasuresInCanonicalMeasureTable()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var orchestrator = new PipelineOrchestrator();

        var result = orchestrator.Run(fixture.Options);

        var factTable = result.Model.Tables.Should().ContainSingle(table => table.Name == "Fact_Pipeline_SampleData").Subject;
        var tablesPath = Path.Combine(fixture.SemanticModelRootPath, "definition", "tables");
        var factPath = Path.Combine(tablesPath, "Fact_Pipeline_SampleData.tmdl");
        var measureTablePath = Path.Combine(tablesPath, "_Measure Table.tmdl");
        var legacyMeasurePath = Path.Combine(tablesPath, "_Measures.tmdl");
        var factText = File.ReadAllText(factPath);
        var measureTableText = File.ReadAllText(measureTablePath);

        foreach (var column in factTable.Columns)
        {
            factText.Should().Contain($"\tcolumn {column.Name}");
        }

        File.Exists(measureTablePath).Should().BeTrue();
        File.Exists(legacyMeasurePath).Should().BeFalse();
        measureTableText.Should().Contain("table '_Measure Table'");
        measureTableText.Should().Contain("measure 'Active Pipelines'");
        measureTableText.Should().Contain("measure 'SLA Breach %'");
        measureTableText.Should().Contain("measure 'Timeline Base'");
        measureTableText.Should().Contain("measure 'Floating Bar Duration'");
        measureTableText.Should().Contain("measure 'SLA Breach Color'");
        factText.Should().NotContain("measure 'Active Pipelines'");
        factText.Should().NotContain("measure 'SLA Breach %'");
        factText.Should().NotContain("measure 'Timeline Base'");
        factText.Should().NotContain("measure 'Floating Bar Duration'");
        factText.Should().NotContain("measure 'SLA Breach Color'");
        factText.Should().Contain("partition Fact_Pipeline_SampleData = m");

        var partitionLine = "\tpartition Fact_Pipeline_SampleData = m";
        var partitionIndex = factText.IndexOf(partitionLine, StringComparison.Ordinal);
        partitionIndex.Should().BeGreaterThanOrEqualTo(0, "the Fact partition must be a direct child of the table");
        factText.Should().NotContain("\npartition Fact_Pipeline_SampleData = m", "the Fact partition must not be root-level");
        factText.Should().Contain("\t\tmode: import", "partition mode must be a direct child of the partition");
        factText.Should().Contain("\t\tsource =", "partition source must be a direct child of the partition");

        var modeIndex = factText.IndexOf("\t\tmode: import", partitionIndex, StringComparison.Ordinal);
        var sourceIndex = factText.IndexOf("\t\tsource =", partitionIndex, StringComparison.Ordinal);
        modeIndex.Should().BeGreaterThan(partitionIndex);
        sourceIndex.Should().BeGreaterThan(partitionIndex);
        factText.Should().NotContain("\n\tmode: import", "partition mode must not be at table scope");
        factText.Should().NotContain("\n\tsource =", "partition source must not be at table scope");
        factText.Should().Contain("File.Contents(DataFolder & \"\\Fact_Pipeline_SampleData.csv\")");

        var modelText = File.ReadAllText(Path.Combine(fixture.SemanticModelRootPath, "definition", "model.tmdl"));
        modelText.Should().Contain("ref table '_Measure Table'");
        modelText.Should().NotContain("ref table _Measures");
    }

    [Fact]
    public void Run_ShouldThrow_WhenDataDirectoryIsMissing()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var options = new PipelineOptions
        {
            DataDirectoryPath = Path.Combine(fixture.DataDirectoryPath, "does-not-exist"),
            SemanticModelTemplateRootPath = fixture.Options.SemanticModelTemplateRootPath,
            SemanticModelRootPath = fixture.Options.SemanticModelRootPath,
            ReportRootPath = fixture.Options.ReportRootPath,
            ReportTemplateRootPath = fixture.Options.ReportTemplateRootPath,
            SemanticModelRelativePath = fixture.Options.SemanticModelRelativePath,
            MetadataOutputPath = fixture.MetadataOutputPath,
            ThrowOnValidationError = fixture.Options.ThrowOnValidationError,
            Logger = fixture.Options.Logger
        };
        var orchestrator = new PipelineOrchestrator();

        Action act = () => orchestrator.Run(options);

        act.Should().Throw<DirectoryNotFoundException>().WithMessage("*Data directory*not found*");
    }

    [Fact]
    public void Run_ShouldThrow_WhenReportTemplateDirectoryIsMissing()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var options = new PipelineOptions
        {
            DataDirectoryPath = fixture.Options.DataDirectoryPath,
            SemanticModelTemplateRootPath = fixture.Options.SemanticModelTemplateRootPath,
            SemanticModelRootPath = fixture.Options.SemanticModelRootPath,
            ReportRootPath = fixture.Options.ReportRootPath,
            ReportTemplateRootPath = Path.Combine(fixture.ReportTemplateRootPath, "missing"),
            SemanticModelRelativePath = fixture.Options.SemanticModelRelativePath,
            MetadataOutputPath = fixture.MetadataOutputPath,
            ThrowOnValidationError = fixture.Options.ThrowOnValidationError,
            Logger = fixture.Options.Logger
        };
        var orchestrator = new PipelineOrchestrator();

        Action act = () => orchestrator.Run(options);

        act.Should().Throw<DirectoryNotFoundException>().WithMessage("*Report template directory*not found*");
    }

    [Fact]
    public void Run_ShouldFailFast_ForInvalidSchemaScenario()
    {
        using var fixture = new PipelineTestFixture("InvalidSchema");
        var orchestrator = new PipelineOrchestrator();

        Action act = () => orchestrator.Run(fixture.Options);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Run_ShouldThrow_WhenSemanticModelTemplateAndOutputPathsAreIdentical()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var options = new PipelineOptions
        {
            DataDirectoryPath = fixture.Options.DataDirectoryPath,
            SemanticModelTemplateRootPath = fixture.Options.SemanticModelTemplateRootPath,
            SemanticModelRootPath = fixture.Options.SemanticModelTemplateRootPath,
            ReportRootPath = fixture.Options.ReportRootPath,
            ReportTemplateRootPath = fixture.Options.ReportTemplateRootPath,
            SemanticModelRelativePath = fixture.Options.SemanticModelRelativePath,
            MetadataOutputPath = fixture.MetadataOutputPath,
            ThrowOnValidationError = fixture.Options.ThrowOnValidationError,
            Logger = fixture.Options.Logger
        };
        var orchestrator = new PipelineOrchestrator();

        Action act = () => orchestrator.Run(options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot equal SemanticModelTemplateRootPath*");
    }
}

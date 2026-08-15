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
            MetadataOutputPath = fixture.Options.MetadataOutputPath,
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
            MetadataOutputPath = fixture.Options.MetadataOutputPath,
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
            MetadataOutputPath = fixture.Options.MetadataOutputPath,
            ThrowOnValidationError = fixture.Options.ThrowOnValidationError,
            Logger = fixture.Options.Logger
        };
        var orchestrator = new PipelineOrchestrator();

        Action act = () => orchestrator.Run(options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SemanticModelTemplateRootPath and SemanticModelRootPath must be different*");
    }
}

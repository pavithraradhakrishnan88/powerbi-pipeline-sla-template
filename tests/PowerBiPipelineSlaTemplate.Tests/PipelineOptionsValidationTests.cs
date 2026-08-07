using System;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class PipelineOptionsValidationTests
{
    [Theory]
    [InlineData(nameof(PipelineOptions.DataDirectoryPath))]
    [InlineData(nameof(PipelineOptions.SemanticModelRootPath))]
    [InlineData(nameof(PipelineOptions.ReportRootPath))]
    [InlineData(nameof(PipelineOptions.ReportTemplateRootPath))]
    [InlineData(nameof(PipelineOptions.SemanticModelRelativePath))]
    [InlineData(nameof(PipelineOptions.MetadataOutputPath))]
    public void Run_ShouldThrow_WhenRequiredOptionIsMissing(string propertyName)
    {
        using var fixture = new PipelineTestFixture("Normal");
        var options = BuildOptionsWithMissingProperty(fixture, propertyName);

        var orchestrator = new PipelineOrchestrator();

        Action act = () => orchestrator.Run(options);

        act.Should().Throw<ArgumentException>();
    }

    private static PipelineOptions BuildOptionsWithMissingProperty(PipelineTestFixture fixture, string propertyName)
    {
        return new PipelineOptions
        {
            DataDirectoryPath = propertyName == nameof(PipelineOptions.DataDirectoryPath) ? string.Empty : fixture.Options.DataDirectoryPath,
            SemanticModelRootPath = propertyName == nameof(PipelineOptions.SemanticModelRootPath) ? string.Empty : fixture.Options.SemanticModelRootPath,
            ReportRootPath = propertyName == nameof(PipelineOptions.ReportRootPath) ? string.Empty : fixture.Options.ReportRootPath,
            ReportTemplateRootPath = propertyName == nameof(PipelineOptions.ReportTemplateRootPath) ? string.Empty : fixture.Options.ReportTemplateRootPath,
            SemanticModelRelativePath = propertyName == nameof(PipelineOptions.SemanticModelRelativePath) ? string.Empty : fixture.Options.SemanticModelRelativePath,
            MetadataOutputPath = propertyName == nameof(PipelineOptions.MetadataOutputPath) ? string.Empty : fixture.Options.MetadataOutputPath,
            ThrowOnValidationError = fixture.Options.ThrowOnValidationError,
            Logger = fixture.Options.Logger
        };
    }
}

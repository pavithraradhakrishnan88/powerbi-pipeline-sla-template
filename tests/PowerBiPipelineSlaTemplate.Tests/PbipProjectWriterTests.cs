using System;
using System.IO;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class PbipProjectWriterTests
{
    [Fact]
    public void Run_ShouldSucceed_ForNormalScenario()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var orchestrator = new PipelineOrchestrator();

        var result = orchestrator.Run(fixture.Options);

        var expectedPbipPath = Path.Combine(
            Path.GetDirectoryName(fixture.ReportRootPath)!,
            Path.GetFileName(fixture.ReportRootPath).Replace(".Report", string.Empty) + ".pbip");
        result.PbipFilePath.Should().Be(expectedPbipPath);
        File.Exists(result.PbipFilePath).Should().BeTrue();

        var pbipContent = File.ReadAllText(result.PbipFilePath);
        pbipContent.Should().Contain("\"version\": \"1.0\"");
        pbipContent.Should().Contain($"\"path\": \"{Path.GetFileName(fixture.ReportRootPath)}\"");
        pbipContent.Should().NotContain("SemanticModel");
    }

    [Fact]
    public void Run_ShouldThrow_WhenReportAndSemanticModelRootsHaveDifferentParents()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var options = new PipelineOptions
        {
            DataDirectoryPath = fixture.Options.DataDirectoryPath,
            SemanticModelTemplateRootPath = fixture.Options.SemanticModelTemplateRootPath,
            SemanticModelRootPath = Path.Combine(Path.GetDirectoryName(fixture.SemanticModelRootPath)!, "nested", Path.GetFileName(fixture.SemanticModelRootPath)),
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
            .WithMessage("*Cannot determine a single PBIP root directory*");
    }
}

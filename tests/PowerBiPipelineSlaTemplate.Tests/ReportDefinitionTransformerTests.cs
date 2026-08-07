using System;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class ReportDefinitionTransformerTests
{
    [Fact]
    public void CreateDefault_ShouldReturnExpectedDefaults()
    {
        var document = ReportDefinitionTransformer.CreateDefault("Pipeline SLA");

        document.Name.Should().Be("Pipeline SLA Report");
        document.Pages.Should().HaveCount(2);
        document.Navigation.DefaultPage.Should().Be("pipeline-overview");
        document.Themes.Should().ContainSingle(theme => theme.Path == "PipelineTheme.json");
    }

    [Fact]
    public void CreateDefault_ShouldThrow_WhenProjectNameIsEmpty()
    {
        Action act = () => ReportDefinitionTransformer.CreateDefault(string.Empty);

        act.Should().Throw<ArgumentException>();
    }
}

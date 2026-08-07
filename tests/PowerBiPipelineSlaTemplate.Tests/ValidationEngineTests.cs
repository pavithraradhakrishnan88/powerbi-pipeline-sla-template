using System;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class ValidationEngineTests
{
    [Fact]
    public void ValidateBuildResult_ShouldNotThrow_WhenNoErrors()
    {
        var builder = new ModelBuilder();
        var model = new ModelBuildResult();

        Action act = () => builder.ValidateBuildResult(model);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateBuildResult_ShouldThrow_WhenRelationshipAndMeasureErrorsExist()
    {
        var builder = new ModelBuilder();
        var model = new ModelBuildResult();
        model.ValidationErrors.Add("Relationship references missing target table: DimX");
        model.ValidationErrors.Add("Invalid measure definition: TotalDuration");

        Action act = () => builder.ValidateBuildResult(model);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Relationship references missing target table*")
            .WithMessage("*Invalid measure definition*");
    }

    [Fact]
    public void ReportWriterValidation_ShouldThrow_ForInvalidReportDefinition()
    {
        var invalidDocument = new ReportDefinitionDocument
        {
            Name = "Invalid Report",
            Pages =
            {
                new ReportPageDefinition
                {
                    PageId = "page-1",
                    Name = "Page1",
                    DisplayName = "Page 1",
                    Order = 0,
                    Canvas = new CanvasDefinition { Width = 1200, Height = 700 }
                }
            },
            Navigation = new NavigationDefinition { DefaultPage = "missing-page" }
        };

        using var workspace = new TemporaryWorkspace();
        var reportPath = workspace.GetPath("output/Report");
        var writer = new PbirReportWriter();

        Action act = () => writer.Write(invalidDocument, reportPath, "../Model");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Navigation.DefaultPage references missing page*");
    }
}

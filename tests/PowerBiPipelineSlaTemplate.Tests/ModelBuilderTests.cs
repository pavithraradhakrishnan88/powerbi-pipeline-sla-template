using System;
using System.IO;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class ModelBuilderTests
{
    [Fact]
    public void Build_ShouldProduceModel_ForNormalMetadata()
    {
        var builder = new ModelBuilder();

        var model = builder.Build(SampleMetadata.Normal());

        model.Tables.Should().HaveCount(2);
        model.Relationships.Should().HaveCount(1);
        model.ValidationErrors.Should().BeEmpty();
    }

    [Fact]
    public void Build_ShouldHandleEmptyMetadata()
    {
        var builder = new ModelBuilder();

        var model = builder.Build(SampleMetadata.Empty());

        model.Tables.Should().BeEmpty();
        model.Relationships.Should().BeEmpty();
    }

    [Fact]
    public void Build_ShouldReportDuplicateColumns_AsValidationError()
    {
        var builder = new ModelBuilder();

        var model = builder.Build(SampleMetadata.WithDuplicateColumns());

        model.ValidationErrors.Should().Contain(error => error.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateBuildResult_ShouldThrow_WhenValidationErrorsExist()
    {
        var builder = new ModelBuilder();
        var model = SampleModel.WithValidationErrors();

        Action act = () => builder.ValidateBuildResult(model);

        act.Should().Throw<InvalidOperationException>().WithMessage("*validation failed*");
    }

    [Fact]
    public void WriteTmdlArtifacts_ShouldCreateExpectedFiles()
    {
        using var workspace = new TemporaryWorkspace();
        var modelRoot = workspace.CreateDirectory("model");
        var builder = new ModelBuilder();
        var model = builder.Build(SampleMetadata.Normal());

        builder.WriteTmdlArtifacts(model, modelRoot);

        File.Exists(Path.Combine(modelRoot, "Relationships.tmdl")).Should().BeTrue();
        Directory.Exists(Path.Combine(modelRoot, "Tables")).Should().BeTrue();
        Directory.Exists(Path.Combine(modelRoot, "Measures")).Should().BeTrue();
    }
}

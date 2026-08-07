using System;
using System.IO;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class PbipSemanticModelWriterTests
{
    [Fact]
    public void WriteSemanticModel_ShouldGenerateSemanticModelArtifacts()
    {
        using var workspace = new TemporaryWorkspace();
        var pbipRoot = workspace.CreateDirectory("pbip");
        var semanticModelPath = Path.Combine(pbipRoot, "Pipeline SLA.SemanticModel");

        var writer = new PbipSemanticModelWriter();
        var model = SampleModel.Normal();

        writer.WriteSemanticModel(model, semanticModelPath);

        File.Exists(Path.Combine(semanticModelPath, "definition.pbism")).Should().BeTrue();
        File.Exists(Path.Combine(semanticModelPath, "definition", "model.tmdl")).Should().BeTrue();
        File.Exists(Path.Combine(pbipRoot, "Pipeline SLA.pbip")).Should().BeTrue();
    }

    [Fact]
    public void Write_ShouldGenerateReport_WhenTemplateExists()
    {
        using var workspace = new TemporaryWorkspace();
        var pbipRoot = workspace.CreateDirectory("pbip");
        var semanticModelPath = Path.Combine(pbipRoot, "Pipeline SLA.SemanticModel");

        var repoRoot = WorkspacePaths.FindRepoRoot();
        var sourceTemplatePath = Path.Combine(repoRoot, "pbip", "Pipeline_SLA_Tracker.Report");
        var destinationTemplatePath = Path.Combine(pbipRoot, "Pipeline_SLA_Tracker.Report");
        Directory.CreateDirectory(destinationTemplatePath);

        foreach (var sourceFile in Directory.GetFiles(sourceTemplatePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceTemplatePath, sourceFile);
            var destination = Path.Combine(destinationTemplatePath, relative);
            var dir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.Copy(sourceFile, destination, overwrite: true);
        }

        var writer = new PbipSemanticModelWriter();

        writer.Write(SampleModel.Normal(), semanticModelPath);

        Directory.Exists(Path.Combine(pbipRoot, "Pipeline SLA.Report")).Should().BeTrue();
        File.Exists(Path.Combine(pbipRoot, "Pipeline SLA.Report", "definition.pbir")).Should().BeTrue();
    }

    [Fact]
    public void WriteSemanticModel_ShouldThrow_ForInvalidInput()
    {
        var writer = new PbipSemanticModelWriter();

        Action act = () => writer.WriteSemanticModel(null!, "");

        act.Should().Throw<ArgumentNullException>();
    }
}

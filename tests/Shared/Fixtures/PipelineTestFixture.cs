using System;
using System.IO;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;

namespace PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;

public sealed class PipelineTestFixture : IDisposable
{
    private readonly TemporaryWorkspace _workspace;

    public PipelineTestFixture(string scenarioName)
    {
        _workspace = new TemporaryWorkspace();

        DataDirectoryPath = _workspace.CreateDirectory("data");
        SampleCsvGenerator.CopyScenarioToWorkspace(scenarioName, DataDirectoryPath);

var pbipRoot = _workspace.CreateDirectory("pbip");
var repoRoot = WorkspacePaths.FindRepoRoot();

var sourceMeasureDefinitionsPath = Path.Combine(
    repoRoot,
    "scripts",
    "metadata",
    "MeasureDefinitions.json");

var destinationMeasureDefinitionsPath = _workspace.GetPath(
    Path.Combine("scripts", "metadata", "MeasureDefinitions.json"));

Directory.CreateDirectory(Path.GetDirectoryName(destinationMeasureDefinitionsPath)!);
File.Copy(sourceMeasureDefinitionsPath, destinationMeasureDefinitionsPath, overwrite: true);

var sourceTemplatePath = Path.Combine(
    repoRoot,
    "pbip",
    "Pipeline_SLA_Tracker.Report");

        ReportTemplateRootPath = Path.Combine(pbipRoot, "Pipeline_SLA_Tracker.Report");
        CopyDirectory(sourceTemplatePath, ReportTemplateRootPath);

        SemanticModelRootPath = Path.Combine(pbipRoot, "Pipeline SLA.SemanticModel");
        ReportRootPath = Path.Combine(pbipRoot, "Pipeline SLA.Report");
        MetadataOutputPath = _workspace.GetPath(Path.Combine("metadata", "metadata.json"));

        Options = new PipelineOptions
        {
            DataDirectoryPath = DataDirectoryPath,
            SemanticModelRootPath = SemanticModelRootPath,
            ReportRootPath = ReportRootPath,
            ReportTemplateRootPath = ReportTemplateRootPath,
            SemanticModelRelativePath = "../Pipeline SLA.SemanticModel",
            MetadataOutputPath = MetadataOutputPath,
            ThrowOnValidationError = true
        };
    }

    public PipelineOptions Options { get; }
    public string DataDirectoryPath { get; }
    public string SemanticModelRootPath { get; }
    public string ReportRootPath { get; }
    public string ReportTemplateRootPath { get; }
    public string MetadataOutputPath { get; }

    public void Dispose()
    {
        _workspace.Dispose();
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Source directory was not found: {sourcePath}");
        }

        Directory.CreateDirectory(destinationPath);

        foreach (var sourceFile in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
    }
}

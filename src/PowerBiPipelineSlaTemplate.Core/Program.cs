using System;
using System.IO;
using PowerBiPipelineSlaTemplate.Core;
using PowerBiPipelineSlaTemplate.Core.Pbip;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var outputPath = Path.Combine(repoRoot, "metadata", "metadata.json");
var dataDirectory = Environment.GetEnvironmentVariable("PBIP_DATA_DIRECTORY");
if (string.IsNullOrWhiteSpace(dataDirectory))
    dataDirectory = Path.Combine(repoRoot, "data");

var generatedPbipRoot = Path.Combine(repoRoot, "BuildResult", "PBIP");
var semanticModelTemplateRoot = Path.Combine(repoRoot, "pbip", "Pipeline_SLA_Tracker.SemanticModel");
var reportTemplateRoot = Path.Combine(repoRoot, "pbip", "Pipeline_SLA_Tracker.Report");
var semanticModelRoot = Path.Combine(generatedPbipRoot, "Pipeline_SLA_Tracker.SemanticModel");
var reportRoot = Path.Combine(generatedPbipRoot, "Pipeline_SLA_Tracker.Report");

if (args.Length >= 1 && string.Equals(args[0], "--extract-metadata", StringComparison.OrdinalIgnoreCase))
{
    for (var index = 1; index < args.Length; index++)
    {
        if (string.Equals(args[index], "--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
        {
            outputPath = Path.GetFullPath(args[index + 1]);
            break;
        }
    }
}
else
{
    for (var index = 0; index < args.Length; index++)
    {
        if (string.Equals(args[index], "--data-directory", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
        {
            dataDirectory = args[++index];
        }
    }
}

dataDirectory = Path.GetFullPath(dataDirectory);
if (!Directory.Exists(dataDirectory))
    throw new DirectoryNotFoundException($"Build data directory was not found: {dataDirectory}");

var options = new PipelineOptions
{
    DataDirectoryPath = dataDirectory,
    SemanticModelRootPath = semanticModelRoot,
    SemanticModelTemplateRootPath = semanticModelTemplateRoot,
    ReportRootPath = reportRoot,
    ReportTemplateRootPath = reportTemplateRoot,
    SemanticModelRelativePath = "../Pipeline_SLA_Tracker.SemanticModel",
    MetadataOutputPath = outputPath,
    ThrowOnValidationError = true,
    Logger = message => Console.WriteLine(message),
};

var orchestrator = new PipelineOrchestrator();
var result = orchestrator.Run(options);
Console.WriteLine("Pipeline completed successfully.");
Console.WriteLine($"Data directory materialized from: {options.DataDirectoryPath}");
Console.WriteLine($"Semantic Model Template: {options.SemanticModelTemplateRootPath}");
Console.WriteLine($"Semantic Model Output: {result.SemanticModelRootPath}");
Console.WriteLine($"Report Output: {result.ReportRootPath}");
Console.WriteLine($"PBIP Project File: {result.PbipFilePath}");

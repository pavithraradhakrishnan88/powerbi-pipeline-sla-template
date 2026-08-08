using System;
using System.IO;
using PowerBiPipelineSlaTemplate.Core;
using PowerBiPipelineSlaTemplate.Core.Pbip;

var outputPath = "metadata/metadata.json";
var dataDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data"));

if (args.Length >= 2 && string.Equals(args[0], "--extract-metadata", StringComparison.OrdinalIgnoreCase))
{
    for (var index = 1; index < args.Length; index++)
    {
        if (string.Equals(args[index], "--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
        {
            outputPath = args[index + 1];
            break;
        }
    }
}

var options = new PipelineOptions

{
    DataDirectoryPath = dataDirectory,
    SemanticModelRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "pbip", "Pipeline_SLA_Tracker.SemanticModel")),
    ReportRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "pbip", "Pipeline_SLA_Tracker.Report")),
    ReportTemplateRootPath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "pbip",
        "Pipeline_SLA_Tracker.Report")),
    SemanticModelRelativePath = "../Pipeline_SLA_Tracker.SemanticModel",
    MetadataOutputPath = outputPath,
    ThrowOnValidationError = true,
    Logger = message => Console.WriteLine(message),
};
var orchestrator = new PipelineOrchestrator();
var result = orchestrator.Run(options);
Console.WriteLine("Pipeline completed successfully.");
Console.WriteLine($"Semantic Model: {result.SemanticModelRootPath}");
Console.WriteLine($"Report: {result.ReportRootPath}");

using System;
using System.IO;
using PowerBiPipelineSlaTemplate.Core.Models;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    /// <summary>
    /// Coordinates metadata extraction, model building, validation, and PBIP artifact generation.
    /// </summary>
    public sealed class PipelineOrchestrator
    {
        /// <summary>
        /// Executes the pipeline.
        /// </summary>
        /// <param name="options">The pipeline configuration.</param>
        /// <returns>The pipeline result.</returns>
        public PipelineResult Run(PipelineOptions options)
        {
            ValidateInput(options);
            var metadata = ExtractMetadata(options);
            var model = BuildModel(metadata);
            ValidateModel(model);
            WriteMetadata(model, options);
            WriteSemanticModel(model, options);
            WriteReport(options);
            ValidateOutput(options);
            return new PipelineResult(metadata, model, options.SemanticModelRootPath, options.ReportRootPath);
        }

        private static void ValidateInput(PipelineOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectoryPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.SemanticModelRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.ReportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.ReportTemplateRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.SemanticModelRelativePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.MetadataOutputPath);

            if (!Directory.Exists(options.DataDirectoryPath))
            {
                throw new DirectoryNotFoundException($"Data directory was not found: {options.DataDirectoryPath}");
            }

            if (!Directory.Exists(options.ReportTemplateRootPath))
            {
                throw new DirectoryNotFoundException($"Report template directory was not found: {options.ReportTemplateRootPath}");
            }
        }

        private static DatabaseSchema ExtractMetadata(PipelineOptions options)
        {
            var schemaReader = new SchemaReader(options.DataDirectoryPath, options.Logger, options.ThrowOnValidationError);
            return schemaReader.Read();
        }

        private static ModelBuildResult BuildModel(DatabaseSchema metadata)
        {
            var modelBuilder = new ModelBuilder();
            return modelBuilder.Build(metadata);
        }

        private static void ValidateModel(ModelBuildResult model)
        {
            var modelBuilder = new ModelBuilder();
            modelBuilder.ValidateBuildResult(model);
        }

        private static void WriteMetadata(ModelBuildResult model, PipelineOptions options)
{
    var modelBuilder = new ModelBuilder();

    var json = modelBuilder.ToJson(model, true);

    var directory = Path.GetDirectoryName(options.MetadataOutputPath);

    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    File.WriteAllText(options.MetadataOutputPath, json);
}

        private static void WriteSemanticModel(ModelBuildResult model, PipelineOptions options)
        {
            var semanticModelWriter = new PbipSemanticModelWriter();
            semanticModelWriter.Write(model, options.SemanticModelRootPath);
        }

        private static void WriteReport(PipelineOptions options)
        {
            var reportWriter = new PbirReportWriter();
            reportWriter.WriteFromTemplate(options.ReportTemplateRootPath, options.ReportRootPath, options.SemanticModelRelativePath);
        }

        private static void ValidateOutput(PipelineOptions options)
        {
            var requiredFiles = new[]
            {
                Path.Combine(options.SemanticModelRootPath, "definition.pbism"),
                Path.Combine(options.SemanticModelRootPath, "definition", "model.tmdl"),
                Path.Combine(options.SemanticModelRootPath, "definition", "database.tmdl"),
                Path.Combine(options.ReportRootPath, "definition.pbir"),
                Path.Combine(options.ReportRootPath, "definition", "report.json"),
                Path.Combine(options.ReportRootPath, "definition", "pages", "pages.json")
            };

            foreach (var filePath in requiredFiles)
            {
                if (!File.Exists(filePath))
                {
                    throw new InvalidOperationException($"Generated pipeline output is missing required file '{filePath}'.");
                }
            }
        }
    }
}
using System;
using System.IO;
using System.Linq;
using PowerBiPipelineSlaTemplate.Core.Models;
using PowerBiPipelineSlaTemplate.Core.Serialization;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    public sealed class PipelineOrchestrator
    {
        public PipelineResult Run(PipelineOptions options)
        {
            ValidateInput(options);
            var metadata = ExtractMetadata(options);
            var model = BuildModel(metadata);
            ValidateModel(model);
            WriteMetadata(model, options);
            WriteSemanticModel(model, options);
            WriteReport(options);
            MigrateAndValidateReportMeasures(options);
            NormalizeAndValidateReportVisuals(options);
            var pbipFilePath = WritePbipProjectFile(options);
            ValidateOutput(model, options, pbipFilePath);
            return new PipelineResult(metadata, model, options.SemanticModelRootPath, options.ReportRootPath, pbipFilePath);
        }

        private static void ValidateInput(PipelineOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectoryPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.SemanticModelRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.SemanticModelTemplateRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.ReportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.ReportTemplateRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.SemanticModelRelativePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.MetadataOutputPath);
            if (!Directory.Exists(options.DataDirectoryPath)) throw new DirectoryNotFoundException($"Data directory was not found: {options.DataDirectoryPath}");
            if (!Directory.Exists(options.SemanticModelTemplateRootPath)) throw new DirectoryNotFoundException($"Semantic model template directory was not found: {options.SemanticModelTemplateRootPath}");
            if (!Directory.Exists(options.ReportTemplateRootPath)) throw new DirectoryNotFoundException($"Report template directory was not found: {options.ReportTemplateRootPath}");
            if (string.Equals(Path.GetFullPath(options.SemanticModelRootPath).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(options.SemanticModelTemplateRootPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SemanticModelRootPath must be a generated output directory and cannot equal SemanticModelTemplateRootPath.");
        }

        private static DatabaseSchema ExtractMetadata(PipelineOptions options) => new SchemaReader(options.DataDirectoryPath, options.Logger, options.ThrowOnValidationError).Read();
        private static ModelBuildResult BuildModel(DatabaseSchema metadata) => new ModelBuilder().Build(metadata);
        private static void ValidateModel(ModelBuildResult model) => new ModelBuilder().ValidateBuildResult(model);

        private static void WriteMetadata(ModelBuildResult model, PipelineOptions options)
        {
            var metadataDocument = new MetadataDocument
            {
                Project = new ProjectMetadata { Name = "Pipeline SLA Tracker", Version = "1.0", Generator = "PowerBiPipelineSlaTemplate", GeneratedOn = DateTimeOffset.UtcNow.ToString("o"), Repository = "powerbi-pipeline-sla-template-git" },
                Summary = new MetadataSummary { TableCount = model.Tables.Count, ColumnCount = model.Tables.Sum(table => table.Columns.Count), RelationshipCount = model.Relationships.Count, FactTables = model.Tables.Count(table => table.IsFactTable), DimensionTables = model.Tables.Count(table => table.IsDimensionTable), MeasureCount = 0, RowCount = model.Tables.Sum(table => table.RowCount) },
                Tables = model.Tables.Select(table => new TableMetadata { Name = table.Name, DisplayFolder = table.DisplayFolder, IsFactTable = table.IsFactTable, IsDimensionTable = table.IsDimensionTable, RowCount = table.RowCount, Columns = table.Columns.Select(column => new ColumnMetadata { Name = column.Name, Type = column.DataType, Nullable = column.Nullable, IsPrimaryKey = column.IsPrimaryKey, IsForeignKey = column.IsForeignKey, Description = column.Description, DatabaseType = column.DataType, PowerBiType = column.DataType, DisplayFolder = column.DisplayFolder, FormatString = column.FormatString }).ToList() }).ToList()
            };
            var directory = Path.GetDirectoryName(options.MetadataOutputPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(options.MetadataOutputPath, MetadataSerializer.ToJson(metadataDocument, true));
        }

        private static void WriteSemanticModel(ModelBuildResult model, PipelineOptions options)
        {
            var dataFolder = Path.GetFullPath(options.DataDirectoryPath);
            var writer = new TemplateSemanticModelWriter();
            writer.Write(options.SemanticModelTemplateRootPath, options.SemanticModelRootPath, dataFolder, model, options.Logger);
        }

        private static void WriteReport(PipelineOptions options) => new PbirReportWriter().WriteFromTemplate(options.ReportTemplateRootPath, options.ReportRootPath, options.SemanticModelRelativePath);

        private static void MigrateAndValidateReportMeasures(PipelineOptions options)
        {
            PbirMeasureReferenceMigrator.MigrateAndValidate(options.ReportRootPath, options.SemanticModelRootPath);
        }

        private static void NormalizeAndValidateReportVisuals(PipelineOptions options)
        {
            // Keep the authoritative template visual tree intact; normalize only model references and reject malformed PBIR.
            PbirVisualContainerNormalizer.NormalizeReport(options.ReportRootPath);
        }

        private static string WritePbipProjectFile(PipelineOptions options)
        {
            var reportParent = Path.GetFullPath(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(options.ReportRootPath)) ?? string.Empty);
            var semanticModelParent = Path.GetFullPath(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(options.SemanticModelRootPath)) ?? string.Empty);
            if (!string.Equals(reportParent.TrimEnd(Path.DirectorySeparatorChar), semanticModelParent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Cannot determine a single PBIP root directory: ReportRootPath's parent ('{reportParent}') differs from SemanticModelRootPath's parent ('{semanticModelParent}').");
            return new PbipProjectWriter().Write(options.ReportRootPath, reportParent);
        }

        private static void ValidateOutput(ModelBuildResult model, PipelineOptions options, string pbipFilePath)
        {
            var requiredFiles = new[] { Path.Combine(options.SemanticModelRootPath, "definition.pbism"), Path.Combine(options.SemanticModelRootPath, "definition", "model.tmdl"), Path.Combine(options.SemanticModelRootPath, "definition", "database.tmdl"), Path.Combine(options.ReportRootPath, "definition.pbir"), Path.Combine(options.ReportRootPath, "definition", "report.json"), Path.Combine(options.ReportRootPath, "definition", "pages", "pages.json"), pbipFilePath };
            foreach (var filePath in requiredFiles) if (!File.Exists(filePath)) throw new InvalidOperationException($"Generated pipeline output is missing required file '{filePath}'.");
            var factPath = Path.Combine(options.SemanticModelRootPath, "definition", "tables", "Fact_Pipeline_SampleData.tmdl");
            if (!File.Exists(factPath)) throw new InvalidOperationException($"Generated pipeline output is missing required fact table '{factPath}'.");
            var factText = File.ReadAllText(factPath);
            var factTable = model.Tables.FirstOrDefault(table => string.Equals(table.Name, "Fact_Pipeline_SampleData", StringComparison.OrdinalIgnoreCase));
            if (factTable is null) throw new InvalidOperationException("Generated model does not contain Fact_Pipeline_SampleData.");
            foreach (var column in factTable.Columns)
            {
                var token = $"\tcolumn {column.Name}";
                if (!factText.Contains(token, StringComparison.Ordinal)) throw new InvalidOperationException($"Generated fact table is missing model column '{column.Name}'.");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            var pbipFilePath = WritePbipProjectFile(options);
            ValidateOutput(options, pbipFilePath);
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
            writer.Write(options.SemanticModelTemplateRootPath, options.SemanticModelRootPath, dataFolder, options.Logger);

            // The authoritative template carries report-specific semantic metadata, but its
            // Fact/Dimension column definitions can lag the current CSV schema. Replace only
            // those generated table definitions from the validated metadata model so every
            // relationship column is guaranteed to exist in the final TMDL database.
            var tempRoot = Path.Combine(Path.GetTempPath(), "PipelineSlaTemplate", Guid.NewGuid().ToString("N"));
            try
            {
                new ModelBuilder().WriteTmdlArtifacts(model, tempRoot);

                foreach (var tableName in new[] { "Fact_Pipeline_SampleData", "Dim_Category" })
                {
                    var generatedTable = Path.Combine(tempRoot, "Tables", $"{tableName}.tmdl");
                    var targetTable = Path.Combine(options.SemanticModelRootPath, "definition", "tables", $"{tableName}.tmdl");
                    if (!File.Exists(generatedTable))
                        throw new InvalidOperationException($"Model generator did not emit expected table TMDL '{tableName}'.");
                    if (!File.Exists(targetTable))
                        throw new InvalidOperationException($"Template semantic model is missing expected table TMDL '{tableName}'.");
                    File.Copy(generatedTable, targetTable, true);
                }
            }
            finally
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }

            ValidateRelationshipGraph(options.SemanticModelRootPath);
        }

        private static void ValidateRelationshipGraph(string semanticModelRootPath)
        {
            var definitionPath = Path.Combine(semanticModelRootPath, "definition");
            var tablesPath = Path.Combine(definitionPath, "tables");
            var relationshipsPath = Path.Combine(definitionPath, "relationships.tmdl");
            if (!File.Exists(relationshipsPath))
                throw new InvalidOperationException("Generated semantic model is missing relationships.tmdl.");

            var tables = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var tableRegex = new Regex(@"(?m)^table\s+([^\r\n]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
            var columnRegex = new Regex(@"(?m)^\s*column\s+([^\r\n]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

            foreach (var file in Directory.GetFiles(tablesPath, "*.tmdl", SearchOption.TopDirectoryOnly))
            {
                var text = File.ReadAllText(file);
                var tableMatch = tableRegex.Match(text);
                if (!tableMatch.Success) continue;
                var tableName = UnquoteTmdlIdentifier(tableMatch.Groups[1].Value.Trim());
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in columnRegex.Matches(text))
                    columns.Add(UnquoteTmdlIdentifier(match.Groups[1].Value.Trim()));
                tables[tableName] = columns;
            }

            var relationshipText = File.ReadAllText(relationshipsPath);
            var relationshipRegex = new Regex(
                @"(?ms)^\s*relationship\s+[^\r\n]+.*?^\s*fromColumn:\s*([^\r\n]+)\s*\r?\n\s*toColumn:\s*([^\r\n]+)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            var errors = new List<string>();
            foreach (Match relationship in relationshipRegex.Matches(relationshipText))
            {
                ValidateRelationshipEndpoint(relationship.Groups[1].Value.Trim(), "FromColumn", tables, errors);
                ValidateRelationshipEndpoint(relationship.Groups[2].Value.Trim(), "ToColumn", tables, errors);
            }

            if (errors.Count > 0)
                throw new InvalidOperationException("Generated semantic model contains dangling relationship column references:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(error => $" - {error}")));
        }

        private static void ValidateRelationshipEndpoint(string endpoint, string property, IReadOnlyDictionary<string, HashSet<string>> tables, ICollection<string> errors)
        {
            var separator = endpoint.LastIndexOf('.');
            if (separator <= 0 || separator >= endpoint.Length - 1)
            {
                errors.Add($"{property} '{endpoint}' is not a valid Table.Column reference.");
                return;
            }

            var tableName = UnquoteTmdlIdentifier(endpoint[..separator].Trim());
            var columnName = UnquoteTmdlIdentifier(endpoint[(separator + 1)..].Trim());
            if (!tables.TryGetValue(tableName, out var columns))
            {
                errors.Add($"{property} references missing table: {tableName}.{columnName}");
                return;
            }

            if (!columns.Contains(columnName))
                errors.Add($"{property} references missing column: {tableName}.{columnName}");
        }

        private static string UnquoteTmdlIdentifier(string value)
        {
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
                return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
            return value;
        }

        private static void WriteReport(PipelineOptions options) => new PbirReportWriter().WriteFromTemplate(options.ReportTemplateRootPath, options.ReportRootPath, options.SemanticModelRelativePath);

        private static void MigrateAndValidateReportMeasures(PipelineOptions options)
        {
            PbirMeasureReferenceMigrator.MigrateAndValidate(options.ReportRootPath, options.SemanticModelRootPath);
        }

        private static string WritePbipProjectFile(PipelineOptions options)
        {
            var reportParent = Path.GetFullPath(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(options.ReportRootPath)) ?? string.Empty);
            var semanticModelParent = Path.GetFullPath(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(options.SemanticModelRootPath)) ?? string.Empty);
            if (!string.Equals(reportParent.TrimEnd(Path.DirectorySeparatorChar), semanticModelParent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Cannot determine a single PBIP root directory: ReportRootPath's parent ('{reportParent}') differs from SemanticModelRootPath's parent ('{semanticModelParent}').");

            return new PbipProjectWriter().Write(options.ReportRootPath, reportParent);
        }

        private static void ValidateOutput(PipelineOptions options, string pbipFilePath)
        {
            var requiredFiles = new[] { Path.Combine(options.SemanticModelRootPath, "definition.pbism"), Path.Combine(options.SemanticModelRootPath, "definition", "model.tmdl"), Path.Combine(options.SemanticModelRootPath, "definition", "database.tmdl"), Path.Combine(options.ReportRootPath, "definition.pbir"), Path.Combine(options.ReportRootPath, "definition", "report.json"), Path.Combine(options.ReportRootPath, "definition", "pages", "pages.json"), pbipFilePath };
            foreach (var filePath in requiredFiles) if (!File.Exists(filePath)) throw new InvalidOperationException($"Generated pipeline output is missing required file '{filePath}'.");
        }
    }
}

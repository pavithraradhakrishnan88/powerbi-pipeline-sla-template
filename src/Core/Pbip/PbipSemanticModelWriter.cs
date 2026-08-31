    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    namespace PowerBiPipelineSlaTemplate.Core.Pbip
    {
        public class PbipSemanticModelWriter
        {
            public void Write(ModelBuildResult model, string semanticModelRootPath)
            {
                ArgumentNullException.ThrowIfNull(model);
                ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRootPath);

                WriteSemanticModel(model, semanticModelRootPath);

                var semanticModelDirectoryName = new DirectoryInfo(semanticModelRootPath).Name;
                var projectBaseName = semanticModelDirectoryName.EndsWith(".SemanticModel", StringComparison.OrdinalIgnoreCase)
                    ? semanticModelDirectoryName[..^".SemanticModel".Length]
                    : semanticModelDirectoryName;
                var pbipRootPath = Path.GetDirectoryName(semanticModelRootPath) ?? semanticModelRootPath;
                var reportRootPath = Path.Combine(
    pbipRootPath,
    $"{projectBaseName}.Report");

var reportTemplatePath = Path.Combine(
    pbipRootPath,
    "Pipeline_SLA_Tracker.Report");

                var reportWriter = new PbirReportWriter();
                reportWriter.WriteFromTemplate(
                    reportTemplatePath,
                    reportRootPath,
                    $"../{projectBaseName}.SemanticModel");
            }

            /// <summary>
            /// Writes the semantic model artifacts without generating the report artifact.
            /// </summary>
            /// <param name="model">The model to write.</param>
            /// <param name="semanticModelRootPath">The semantic model root directory.</param>
            public void WriteSemanticModel(ModelBuildResult model, string semanticModelRootPath)
            {
                ArgumentNullException.ThrowIfNull(model);
                ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRootPath);

                var semanticModelDirectoryName = new DirectoryInfo(semanticModelRootPath).Name;
                var projectBaseName = semanticModelDirectoryName.EndsWith(".SemanticModel", StringComparison.OrdinalIgnoreCase)
                    ? semanticModelDirectoryName[..^".SemanticModel".Length]
                    : semanticModelDirectoryName;
                var pbipRootPath = Path.GetDirectoryName(semanticModelRootPath) ?? semanticModelRootPath;


                var definitionPath = Path.Combine(semanticModelRootPath, "definition");
                var tablesPath = Path.Combine(definitionPath, "tables");
                var culturesPath = Path.Combine(definitionPath, "cultures");
                var repositoryRootPath = Directory.GetParent(pbipRootPath)?.FullName ?? pbipRootPath;
                var dataDirectoryPath = Path.Combine(repositoryRootPath, "data");

                Directory.CreateDirectory(semanticModelRootPath);
Directory.CreateDirectory(definitionPath);

// Remove legacy generated layout from previous runs.
DeleteIfExists(Path.Combine(semanticModelRootPath, "tables"));
DeleteIfExists(Path.Combine(semanticModelRootPath, "measures"));
DeleteIfExists(Path.Combine(semanticModelRootPath, "relationships"));
DeleteIfExists(Path.Combine(semanticModelRootPath, "model.tmdl"));

// Clean generated definition artifacts.
DeleteIfExists(culturesPath);
DeleteIfExists(Path.Combine(definitionPath, "relationships.tmdl"));
DeleteIfExists(Path.Combine(definitionPath, "database.tmdl"));
DeleteIfExists(Path.Combine(definitionPath, "expressions.tmdl"));
DeleteIfExists(Path.Combine(definitionPath, "model.tmdl"));

Directory.CreateDirectory(tablesPath);
Directory.CreateDirectory(culturesPath);

                foreach (var table in model.Tables)
                {
                    var tableFilePath = Path.Combine(tablesPath, $"{SanitizeFileName(table.Name)}.tmdl");
                    File.WriteAllText(tableFilePath, BuildTableTmdl(table));
                }

                var relationshipFilePath = Path.Combine(definitionPath, "relationships.tmdl");
                File.WriteAllText(relationshipFilePath, BuildRelationshipsTmdl(model.Relationships));

            var measuresTablePath = Path.Combine(tablesPath, "_Measure Table.tmdl");
                var measureDefinitionsPath = Path.Combine(
                repositoryRootPath,
                "scripts",
                "metadata",
                "MeasureDefinitions.json");

                File.WriteAllText(
                measuresTablePath,
                BuildMeasuresTableTmdl(measureDefinitionsPath, model.Tables));

                File.WriteAllText(Path.Combine(definitionPath, "database.tmdl"), BuildDatabaseTmdl(model));
                File.WriteAllText(Path.Combine(definitionPath, "expressions.tmdl"), BuildExpressionsTmdl(dataDirectoryPath));
                File.WriteAllText(Path.Combine(definitionPath, "model.tmdl"), BuildModelTmdl(model));
                File.WriteAllText(Path.Combine(culturesPath, "en-US.tmdl"), BuildCultureTmdl());
                File.WriteAllText(Path.Combine(semanticModelRootPath, "definition.pbism"), BuildPbism(model));

                File.WriteAllText(Path.Combine(pbipRootPath, $"{projectBaseName}.pbip"), BuildPbipPointer(projectBaseName));
            }

            private static string BuildModelTmdl(ModelBuildResult model)
            {
                var builder = new StringBuilder();
                builder.AppendLine("model Model");
                builder.AppendLine("\tculture: en-US");
                builder.AppendLine("\tdefaultPowerBIDataSourceVersion: powerBI_V3");
                builder.AppendLine("\tsourceQueryCulture: en-US");
                builder.AppendLine();

                foreach (var table in model.Tables)
                {
                    builder.AppendLine($"ref table {SanitizeObjectName(table.Name)}");
                }

                builder.AppendLine("ref table '_Measure Table'");
                builder.AppendLine();
                builder.AppendLine("ref cultureInfo en-US");
                return builder.ToString();
            }

            private static string BuildPbism(ModelBuildResult model)
            {
                _ = model;

                return "{" + Environment.NewLine
                    + "  \"version\": \"4.2\"," + Environment.NewLine
                    + "  \"settings\": {}" + Environment.NewLine
                    + "}";
            }

            private static string BuildPbipPointer(string projectBaseName)
    {
        return "{" + Environment.NewLine
            + "  \"version\": \"1.0\"," + Environment.NewLine
            + "  \"artifacts\": [" + Environment.NewLine
            + "    {" + Environment.NewLine
            + "      \"report\": {" + Environment.NewLine
            + "        \"path\": \"" + EscapeJsonString($"{projectBaseName}.Report") + "\"" + Environment.NewLine
            + "      }" + Environment.NewLine
            + "    }" + Environment.NewLine
            + "  ]," + Environment.NewLine
            + "  \"settings\": {" + Environment.NewLine
            + "    \"enableAutoRecovery\": true" + Environment.NewLine
            + "  }" + Environment.NewLine
            + "}";
    }

            private static string BuildTableTmdl(BuiltTable table)
            {
                var builder = new StringBuilder();
                var tableName = SanitizeObjectName(table.Name);
                builder.AppendLine($"table {tableName}");

                foreach (var column in table.Columns)
                {
                    var columnName = SanitizeObjectName(column.Name);
                    builder.AppendLine($"\tcolumn {columnName}");
                    builder.AppendLine($"\t\tdataType: {MapTmdlDataType(column.DataType)}");
                    builder.AppendLine($"\t\tsummarizeBy: {(IsNumericColumn(column) ? "sum" : "none")}");
                    builder.AppendLine($"\t\tsourceColumn: {columnName}");

                    if (!string.IsNullOrWhiteSpace(column.DisplayFolder))
                    {
                        builder.AppendLine($"\t\tdisplayFolder: '{EscapeSingleQuotes(column.DisplayFolder)}'");
                    }

                    if (!string.IsNullOrWhiteSpace(column.FormatString))
                    {
                        builder.AppendLine($"\t\tformatString: '{EscapeSingleQuotes(column.FormatString)}'");
                    }

                    if (!string.IsNullOrWhiteSpace(column.Description))
                    {
                        builder.AppendLine($"\t\tannotation Description = '{EscapeSingleQuotes(column.Description)}'");
                    }

                    builder.AppendLine();
                }

                builder.AppendLine($"\tpartition {tableName} = m");
                builder.AppendLine("\t\tmode: import");
                builder.AppendLine("\t\tsource =");
                builder.AppendLine("\t\t\tlet");
                builder.AppendLine($"\t\t\t\tSource = Csv.Document(File.Contents(DataFolder & \"\\{EscapeMString(table.Name)}.csv\"), [Delimiter=\",\", Encoding=65001, QuoteStyle=QuoteStyle.Csv]),");
                builder.AppendLine("\t\t\t\t#\"Promoted Headers\" = Table.PromoteHeaders(Source, [PromoteAllScalars=true])");
                builder.AppendLine("\t\t\tin");
                builder.AppendLine("\t\t\t\t#\"Promoted Headers\"");

                return builder.ToString();
            }

            private static string BuildRelationshipsTmdl(IReadOnlyList<ModelRelationship> relationships)
            {
                var builder = new StringBuilder();
                var index = 1;
                foreach (var relationship in relationships)
                {
                    builder.AppendLine($"relationship r{index:000}");
                    builder.AppendLine($"\tfromColumn: {SanitizeObjectName(relationship.FromTable)}.{SanitizeObjectName(relationship.FromColumn)}");
                    builder.AppendLine($"\ttoColumn: {SanitizeObjectName(relationship.ToTable)}.{SanitizeObjectName(relationship.ToColumn)}");
                    builder.AppendLine();
                    index++;
                }

                return builder.ToString();
            }

            private static Dictionary<string, List<MeasureCandidate>> BuildMeasureGroups(IReadOnlyList<BuiltTable> tables)
            {
                var groups = new Dictionary<string, List<MeasureCandidate>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SLA"] = new List<MeasureCandidate>(),
                    ["Duration"] = new List<MeasureCandidate>(),
                    ["KPIs"] = new List<MeasureCandidate>()
                };

                foreach (var table in tables.Where(table => table.IsFactTable))
                {
                    foreach (var column in table.Columns.Where(IsNumericColumn).Where(column => !ShouldSkipGenericMeasure(column)))
                    {
                        var name = column.Name ?? string.Empty;
                        var normalizedName = name.ToLowerInvariant();
                        var candidate = new MeasureCandidate(table.Name, name);

                        if (normalizedName.Contains("sla") || normalizedName.Contains("breach") || normalizedName.Contains("compliance") || normalizedName.Contains("target"))
                        {
                            groups["SLA"].Add(candidate);
                            continue;
                        }

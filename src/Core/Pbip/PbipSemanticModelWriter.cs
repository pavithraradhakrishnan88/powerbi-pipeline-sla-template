using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    public class PbipSemanticModelWriter
    {
        public void Write(ModelBuildResult model, string semanticModelRootPath)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRootPath);

            var semanticModelDirectoryName = new DirectoryInfo(semanticModelRootPath).Name;
            var projectBaseName = semanticModelDirectoryName.EndsWith(".SemanticModel", StringComparison.OrdinalIgnoreCase)
                ? semanticModelDirectoryName[..^".SemanticModel".Length]
                : semanticModelDirectoryName;
            var pbipRootPath = Path.GetDirectoryName(semanticModelRootPath) ?? semanticModelRootPath;
            var reportRootPath = Path.Combine(pbipRootPath, $"{projectBaseName}.Report");
            var reportTemplatePath = Path.Combine(pbipRootPath, "Pipeline_SLA_Tracker.Report");

            var definitionPath = Path.Combine(semanticModelRootPath, "definition");
            var tablesPath = Path.Combine(definitionPath, "tables");
            var culturesPath = Path.Combine(definitionPath, "cultures");
            var repositoryRootPath = Directory.GetParent(pbipRootPath)?.FullName ?? pbipRootPath;
            var dataDirectoryPath = Path.Combine(repositoryRootPath, "data");

            Directory.CreateDirectory(semanticModelRootPath);
            Directory.CreateDirectory(definitionPath);
            Directory.CreateDirectory(tablesPath);
            Directory.CreateDirectory(culturesPath);

            // Remove legacy generated layout from previous runs.
            DeleteIfExists(Path.Combine(semanticModelRootPath, "tables"));
            DeleteIfExists(Path.Combine(semanticModelRootPath, "measures"));
            DeleteIfExists(Path.Combine(semanticModelRootPath, "relationships"));
            DeleteIfExists(Path.Combine(semanticModelRootPath, "model.tmdl"));

            foreach (var table in model.Tables)
            {
                var tableFilePath = Path.Combine(tablesPath, $"{SanitizeFileName(table.Name)}.tmdl");
                File.WriteAllText(tableFilePath, BuildTableTmdl(table, dataDirectoryPath));
            }

            var relationshipFilePath = Path.Combine(definitionPath, "relationships.tmdl");
            File.WriteAllText(relationshipFilePath, BuildRelationshipsTmdl(model.Relationships));

            var groupedMeasures = BuildMeasureGroups(model.Tables);
            var measuresTablePath = Path.Combine(tablesPath, "_Measures.tmdl");
            File.WriteAllText(measuresTablePath, BuildMeasuresTableTmdl(groupedMeasures));

            File.WriteAllText(Path.Combine(definitionPath, "database.tmdl"), BuildDatabaseTmdl(model));
            File.WriteAllText(Path.Combine(definitionPath, "expressions.tmdl"), BuildExpressionsTmdl());
            File.WriteAllText(Path.Combine(definitionPath, "model.tmdl"), BuildModelTmdl(model));
            File.WriteAllText(Path.Combine(culturesPath, "en-US.tmdl"), BuildCultureTmdl());
            File.WriteAllText(Path.Combine(semanticModelRootPath, "definition.pbism"), BuildPbism(model));

            File.WriteAllText(Path.Combine(pbipRootPath, $"{projectBaseName}.pbip"), BuildPbipPointer(projectBaseName));

            var reportWriter = new PbirReportWriter();
            reportWriter.WriteFromTemplate(
                reportTemplatePath,
                reportRootPath,
                $"../{projectBaseName}.SemanticModel");
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

            builder.AppendLine("ref table _Measures");
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
                + "    }," + Environment.NewLine
                + "    {" + Environment.NewLine
                + "      \"semanticModel\": {" + Environment.NewLine
                + "        \"path\": \"" + EscapeJsonString($"{projectBaseName}.SemanticModel") + "\"" + Environment.NewLine
                + "      }" + Environment.NewLine
                + "    }" + Environment.NewLine
                + "  ]," + Environment.NewLine
                + "  \"settings\": {" + Environment.NewLine
                + "    \"enableAutoRecovery\": true" + Environment.NewLine
                + "  }" + Environment.NewLine
                + "}";
        }

        private static string BuildTableTmdl(BuiltTable table, string dataDirectoryPath)
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

            var tableCsvPath = Path.Combine(dataDirectoryPath, $"{table.Name}.csv");
            builder.AppendLine($"\tpartition {tableName} = m");
            builder.AppendLine("\t\tmode: import");
            builder.AppendLine("\t\tsource =");
            builder.AppendLine("\t\t\t\tlet");
            builder.AppendLine($"\t\t\t\t    Source = Csv.Document(File.Contents(\"{EscapeMString(tableCsvPath)}\"), [Delimiter=\",\", Encoding=65001, QuoteStyle=QuoteStyle.Csv]),");
            builder.AppendLine("\t\t\t\t    #\"Promoted Headers\" = Table.PromoteHeaders(Source, [PromoteAllScalars=true])");
            builder.AppendLine("\t\t\t\tin");
            builder.AppendLine("\t\t\t\t    #\"Promoted Headers\"");

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
                foreach (var column in table.Columns.Where(IsNumericColumn))
                {
                    var name = column.Name ?? string.Empty;
                    var normalizedName = name.ToLowerInvariant();
                    var candidate = new MeasureCandidate(table.Name, name);

                    if (normalizedName.Contains("sla") || normalizedName.Contains("breach") || normalizedName.Contains("compliance") || normalizedName.Contains("target"))
                    {
                        groups["SLA"].Add(candidate);
                        continue;
                    }

                    if (normalizedName.Contains("duration") || normalizedName.Contains("time") || normalizedName.Contains("hour") || normalizedName.Contains("minute") || normalizedName.Contains("second"))
                    {
                        groups["Duration"].Add(candidate);
                        continue;
                    }

                    groups["KPIs"].Add(candidate);
                }
            }

            return groups;
        }

        private static string BuildMeasuresTableTmdl(IReadOnlyDictionary<string, List<MeasureCandidate>> groupedMeasures)
        {
            var builder = new StringBuilder();
            builder.AppendLine("table _Measures");

            foreach (var group in groupedMeasures)
            {
                builder.AppendLine($"\t/// Group: {group.Key}");

                if (group.Value.Count == 0)
                {
                    builder.AppendLine("\t/// No inferred numeric columns available for this group.");
                    builder.AppendLine();
                    continue;
                }

                foreach (var measure in group.Value)
                {
                    var measureName = $"Total {HumanizeName(measure.ColumnName)}";
                    builder.AppendLine($"\tmeasure '{EscapeSingleQuotes(measureName)}' = SUM({SanitizeObjectName(measure.TableName)}[{SanitizeObjectName(measure.ColumnName)}])");
                    builder.AppendLine($"\t\tdisplayFolder: {group.Key}");
                }

                builder.AppendLine();
            }

            builder.AppendLine("\tpartition _Measures = m");
            builder.AppendLine("\t\tmode: import");
            builder.AppendLine("\t\tsource =");
            builder.AppendLine("\t\t\t\tlet");
            builder.AppendLine("\t\t\t\t    Source = #table({}, {})");
            builder.AppendLine("\t\t\t\tin");
            builder.AppendLine("\t\t\t\t    Source");

            return builder.ToString();
        }

        private static string BuildDatabaseTmdl(ModelBuildResult model)
        {
            _ = model;

            return "database" + Environment.NewLine
                + "\tcompatibilityLevel: 1601" + Environment.NewLine;
        }

        private static string BuildExpressionsTmdl()
        {
            return string.Empty;
        }

        private static string BuildCultureTmdl()
        {
            return "cultureInfo en-US" + Environment.NewLine;
        }

        private static bool IsNumericColumn(BuiltColumn column)
        {
            return string.Equals(column.DataType, "Int64", StringComparison.OrdinalIgnoreCase)
                || string.Equals(column.DataType, "Decimal", StringComparison.OrdinalIgnoreCase)
                || string.Equals(column.DataType, "Double", StringComparison.OrdinalIgnoreCase);
        }

        private static string EscapeSingleQuotes(string value)
        {
            return value.Replace("'", "''", StringComparison.Ordinal);
        }

        private static string EscapeJsonString(string value)
        {
            return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        private static string EscapeMString(string value)
        {
            return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\"\"", StringComparison.Ordinal);
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "ModelObject";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "ModelObject" : cleaned;
        }

        private static string HumanizeName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "column";
            }

            return string.Join(" ", value.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
        }

        private static string MapTmdlDataType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return "string";
            }

            return type.Trim().ToLowerInvariant() switch
            {
                "int64" => "int64",
                "double" => "double",
                "decimal" => "decimal",
                "datetime" => "dateTime",
                "date" => "dateTime",
                "boolean" => "boolean",
                "bool" => "boolean",
                _ => "string"
            };
        }

        private static string SanitizeObjectName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Object";
            }

            var sanitized = new string(value.Where(character => char.IsLetterOrDigit(character) || character == '_').ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "Object" : sanitized;
        }

        private sealed class MeasureCandidate
        {
            public MeasureCandidate(string tableName, string columnName)
            {
                TableName = tableName;
                ColumnName = columnName;
            }

            public string TableName { get; }
            public string ColumnName { get; }
        }

        private static void DeleteIfExists(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

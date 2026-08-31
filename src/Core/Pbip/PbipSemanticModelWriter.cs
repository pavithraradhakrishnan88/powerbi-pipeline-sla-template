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
                builder.AppendLine("\t\t\t\tlet");
                builder.AppendLine($"\t\t\t\t    Source = Csv.Document(File.Contents(DataFolder & \"\\{EscapeMString(table.Name)}.csv\"), [Delimiter=\",\", Encoding=65001, QuoteStyle=QuoteStyle.Csv]),");
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

            private static string BuildMeasuresTableTmdl(
                string measureDefinitionsPath,
                IReadOnlyList<BuiltTable> tables)
            {
                _ = tables;
                if (!File.Exists(measureDefinitionsPath))
                {
                    throw new FileNotFoundException(
                        $"Measure definitions file not found: {measureDefinitionsPath}");
                }

                using var document = JsonDocument.Parse(File.ReadAllText(measureDefinitionsPath));

                if (!document.RootElement.TryGetProperty("measures", out var measuresElement) ||
                    measuresElement.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException(
                        "MeasureDefinitions.json must contain a 'measures' array.");
                }

                var measureDefinitions = measuresElement
    .EnumerateArray()
    .ToList();

var measures = measureDefinitions
    .Select(measure =>
    {
        var id = GetJsonString(measure, "MeasureID");
        var name = GetJsonString(measure, "Name");
        var expression = GetJsonString(measure, "Expression");

        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException(
                "Measure definition has an empty MeasureID.");

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException(
                $"Measure '{id}' has an empty Name.");

        if (string.IsNullOrWhiteSpace(expression))
            throw new InvalidDataException(
                $"Measure '{id}' ({name}) has an empty Expression.");

        return new
        {
            Id = id,
            Name = name,
            Folder = GetJsonString(measure, "Folder"),
            DisplayOrder =
                measure.TryGetProperty("DisplayOrder", out var order) &&
                order.ValueKind == JsonValueKind.Number
                    ? order.GetInt32()
                    : int.MaxValue,
            Expression = expression,
            Format = GetJsonString(measure, "Format"),
            Description = GetJsonString(measure, "Description"),
            Hidden =
                measure.TryGetProperty("Hidden", out var hidden) &&
                hidden.ValueKind == JsonValueKind.True &&
                hidden.GetBoolean()
        };
    })
    .ToList();

var duplicateIds = measures
    .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
    .Where(g => g.Count() > 1)
    .Select(g => g.Key)
    .ToList();

if (duplicateIds.Count > 0)
    throw new InvalidDataException(
        $"Duplicate MeasureID values: {string.Join(", ", duplicateIds)}.");

var duplicateNames = measures
    .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
    .Where(g => g.Count() > 1)
    .Select(g => g.Key)
    .ToList();

if (duplicateNames.Count > 0)
    throw new InvalidDataException(
        $"Duplicate measure names: {string.Join(", ", duplicateNames)}.");

if (measures.Count != measureDefinitions.Count)
    throw new InvalidDataException(
        $"Expected {measureDefinitions.Count} measures from " +
        $"MeasureDefinitions.json, but wrote {measures.Count}. " +
        "Check for skipped/filtered entries.");

                var expectedMeasureIds = measureDefinitions
    .Select(m => GetJsonString(m, "MeasureID"))
    .Where(id => !string.IsNullOrWhiteSpace(id))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var actualMeasureIds = measures
                    .Select(m => m.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var missingMeasureIds = expectedMeasureIds
                    .Except(actualMeasureIds, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id)
                    .ToList();

                if (missingMeasureIds.Count > 0)
                {
                    throw new InvalidDataException(
                        $"Missing metadata-defined measures: {string.Join(", ", missingMeasureIds)}");
                }

                if (measures.Count == 0)
                {
                    throw new InvalidDataException(
                        "MeasureDefinitions.json contains no valid measures.");
                }

                var builder = new StringBuilder();
                builder.AppendLine("table '_Measure Table'");
                builder.AppendLine();

                foreach (var measure in measures)
                {
                    var expression = Regex.Replace(
    measure.Expression.Trim(),
    @"\s*\r?\n\s*",
    " ");

builder.AppendLine(
    $"\tmeasure '{EscapeSingleQuotes(measure.Name)}' = {expression}");

                    if (!string.IsNullOrWhiteSpace(measure.Folder))
                    {
                        builder.AppendLine(
                            $"\t\tdisplayFolder: '{EscapeSingleQuotes(measure.Folder)}'");
                    }

                    if (!string.IsNullOrWhiteSpace(measure.Format))
                    {
                        builder.AppendLine(
                            $"\t\tformatString: '{EscapeSingleQuotes(measure.Format)}'");
                    }


                    if (measure.Hidden)
                    {
                        builder.AppendLine("\t\tisHidden: true");
                    }

                    builder.AppendLine();
                }

                builder.AppendLine("\tpartition '_Measure Table' = m");
                builder.AppendLine("\t\tmode: import");
                builder.AppendLine("\t\tsource =");
                builder.AppendLine("\t\t\t\tlet");
                builder.AppendLine("\t\t\t\t    Source = #table({}, {})");
                builder.AppendLine("\t\t\t\tin");
                builder.AppendLine("\t\t\t\t    Source");

                return builder.ToString();
            }

            private static string GetJsonString(
                JsonElement element,
                string propertyName)
            {
                if (!element.TryGetProperty(propertyName, out var property))
                {
                    return string.Empty;
                }

                return property.ValueKind == JsonValueKind.String
                    ? property.GetString() ?? string.Empty
                    : string.Empty;
            }

            private static string BuildDatabaseTmdl(ModelBuildResult model)
            {
                _ = model;

                return "database" + Environment.NewLine
                    + "\tcompatibilityLevel: 1601" + Environment.NewLine;
            }

            private static string BuildExpressionsTmdl(string dataDirectoryPath)
            {
                var builder = new StringBuilder();
                builder.AppendLine(
                    $"expression DataFolder = \"{EscapeMString(dataDirectoryPath)}\" meta [IsParameterQuery=true, Type=\"Text\", IsParameterQueryRequired=true]");
                return builder.ToString();
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

            private static bool ShouldSkipGenericMeasure(BuiltColumn column)
            {
                if (column is null)
                {
                    return false;
                }

                var name = column.Name ?? string.Empty;
                var normalizedName = name.ToLowerInvariant();

                if (column.IsPrimaryKey || column.IsForeignKey)
                {
                    return true;
                }

                if (normalizedName.Contains("id") || normalizedName.Contains("key") || normalizedName.Contains("pk") || normalizedName.Contains("fk"))
                {
                    return true;
                }

                return false;
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

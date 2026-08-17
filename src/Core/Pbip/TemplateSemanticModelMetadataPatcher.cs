using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PowerBiPipelineSlaTemplate.Core;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    /// <summary>
    /// Applies only semantic-model metadata that the authoritative template is missing.
    /// The template remains the source of truth; this class never rebuilds report artifacts.
    /// </summary>
    internal static class TemplateSemanticModelMetadataPatcher
    {
        private const string FactTable = "Fact_Pipeline_SampleData";
        private const string DimensionTable = "Dim_Category";
        private const string FactColumn = "Category";
        private const string DimensionColumn = "CategoryName";

        private static readonly IReadOnlyDictionary<string, string> DateVariationColumns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ScheduledStart"] = "LocalDateTable_9043e032-67a4-45e7-bf88-28fec57966b9",
                ["ActualStart"] = "LocalDateTable_ac53fd01-924f-4452-a3f3-a0f9a1df973c",
                ["ScheduledEnd"] = "LocalDateTable_d4583ee4-86f0-47d3-96ae-303f0614bf0d",
                ["ActualEnd"] = "LocalDateTable_1c11b445-5c42-44a6-9f02-0bc10f99ee27"
            };

        private static readonly IReadOnlyDictionary<string, string> DateVariationRelationships =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ScheduledStart"] = "db2083da-0a18-4176-ab57-a096ce539554",
                ["ActualStart"] = "c7324c3f-573c-4a3b-9563-7a1dcc4b99a3",
                ["ScheduledEnd"] = "5fcd5823-e6b0-472e-bf09-57997645718c",
                ["ActualEnd"] = "6bbc152f-49b4-4e1a-ad36-e50fd87530d8"
            };

        public static void Patch(ModelBuildResult model, string semanticModelRootPath, string repositoryRootPath, Action<string>? logger = null)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRootPath);

            var definition = Path.Combine(semanticModelRootPath, "definition");
            var tables = Path.Combine(definition, "tables");
            var factPath = Path.Combine(tables, $"{FactTable}.tmdl");
            var modelPath = Path.Combine(definition, "model.tmdl");
            var relationshipsPath = Path.Combine(definition, "relationships.tmdl");
            var expressionsPath = Path.Combine(definition, "expressions.tmdl");
            var measureDefinitionsPath = Path.Combine(repositoryRootPath, "scripts", "metadata", "MeasureDefinitions.json");

            if (!File.Exists(factPath)) throw new FileNotFoundException("Fact table template is missing.", factPath);
            if (!File.Exists(relationshipsPath)) throw new FileNotFoundException("Template is missing relationships.tmdl.", relationshipsPath);
            if (!File.Exists(measureDefinitionsPath)) throw new FileNotFoundException("MeasureDefinitions.json is missing.", measureDefinitionsPath);

            PatchColumns(model, factPath, logger);
            PatchDateVariations(factPath, relationshipsPath, logger);
            PatchMeasures(factPath, measureDefinitionsPath, logger);
            EnsureFactPartition(factPath, logger);
            PatchCategoryRelationship(relationshipsPath, logger);
            RemoveLegacyMeasuresTable(tables, modelPath, logger);
            RemoveDanglingMeasureTableAnnotation(modelPath, expressionsPath, logger);
        }

        private static void PatchColumns(ModelBuildResult model, string factPath, Action<string>? logger)
        {
            var factTable = model.Tables.FirstOrDefault(table => string.Equals(table.Name, FactTable, StringComparison.OrdinalIgnoreCase));
            if (factTable is null)
                throw new InvalidDataException($"ModelBuildResult does not contain required fact table '{FactTable}'.");

            var text = File.ReadAllText(factPath, Encoding.UTF8);
            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("column ", StringComparison.OrdinalIgnoreCase)) continue;
                var name = trimmed[7..].Trim();
                if (name.Length >= 2 && name[0] == '\'' && name[^1] == '\'') name = name[1..^1];
                var firstWhitespace = name.IndexOfAny(new[] { ' ', '\t' });
                if (firstWhitespace >= 0) name = name[..firstWhitespace];
                existingColumns.Add(name);
            }

            var columnBlocks = new List<string>();
            var addedCount = 0;
            foreach (var column in factTable.Columns)
            {
                if (existingColumns.Contains(column.Name)) continue;
                columnBlocks.Add(BuildColumnBlock(column));
                existingColumns.Add(column.Name);
                addedCount++;
                logger?.Invoke($"SEMANTIC-MODEL-PATCH|Added column|{FactTable}.{column.Name}");
            }

            if (addedCount == 0)
            {
                logger?.Invoke($"SEMANTIC-MODEL-PATCH|Columns|Fact={FactTable}|Added=0|Existing={existingColumns.Count}");
                return;
            }

            var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var additions = string.Join(newline + newline, columnBlocks).TrimEnd('\r', '\n');
            var firstChildIndex = text.IndexOf("\t/// ", StringComparison.Ordinal);
            if (firstChildIndex < 0) firstChildIndex = text.IndexOf("\tmeasure ", StringComparison.Ordinal);
            if (firstChildIndex < 0) firstChildIndex = text.IndexOf("\tpartition ", StringComparison.Ordinal);
            if (firstChildIndex < 0) firstChildIndex = text.Length;

            var prefix = text[..firstChildIndex].TrimEnd('\r', '\n');
            var suffix = text[firstChildIndex..].TrimStart('\r', '\n');
            text = prefix + newline + newline + additions + newline + newline + suffix;
            File.WriteAllText(factPath, text, new UTF8Encoding(false));
            logger?.Invoke($"SEMANTIC-MODEL-PATCH|Columns|Fact={FactTable}|Added={addedCount}|Total={existingColumns.Count}");
        }

        private static string BuildColumnBlock(BuiltColumn column)
        {
            var builder = new StringBuilder();
            var columnName = SanitizeObjectName(column.Name);
            builder.AppendLine($"\tcolumn {columnName}");
            builder.AppendLine($"\t\tdataType: {MapTmdlDataType(column.DataType)}");
            builder.AppendLine($"\t\tsummarizeBy: {(IsNumericColumn(column) ? "sum" : "none")}");
            builder.AppendLine($"\t\tsourceColumn: {columnName}");
            if (!string.IsNullOrWhiteSpace(column.DisplayFolder)) builder.AppendLine($"\t\tdisplayFolder: '{EscapeSingleQuotes(column.DisplayFolder)}'");
            if (!string.IsNullOrWhiteSpace(column.FormatString)) builder.AppendLine($"\t\tformatString: '{EscapeSingleQuotes(column.FormatString)}'");
            if (!string.IsNullOrWhiteSpace(column.Description)) builder.Append($"\t\tannotation Description = '{EscapeSingleQuotes(column.Description)}'");
            return builder.ToString().TrimEnd('\r', '\n');
        }

        private static void PatchDateVariations(string factPath, string relationshipsPath, Action<string>? logger)
        {
            var text = File.ReadAllText(factPath, Encoding.UTF8);
            var relationships = File.ReadAllText(relationshipsPath, Encoding.UTF8);
            var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

            foreach (var pair in DateVariationColumns)
            {
                var columnName = pair.Key;
                var localDateTable = pair.Value;
                if (!DateVariationRelationships.TryGetValue(columnName, out var relationshipId))
                    continue;

                var relationshipPattern = $"relationship\\s+{Regex.Escape(relationshipId)}\\s+\\r?\\n\\s*joinOnDateBehavior:\\s*datePartOnly\\s+\\r?\\n\\s*fromColumn:\\s*{Regex.Escape(FactTable)}\\.{Regex.Escape(columnName)}\\s+\\r?\\n\\s*toColumn:\\s*{Regex.Escape(localDateTable)}\\.Date";
                if (!Regex.IsMatch(relationships, relationshipPattern, RegexOptions.CultureInvariant))
                    throw new InvalidDataException($"Expected date relationship '{relationshipId}' for {FactTable}.{columnName} was not found.");

                var marker = $"\tcolumn {SanitizeObjectName(columnName)}";
                var start = text.IndexOf(marker, StringComparison.Ordinal);
                if (start < 0) throw new InvalidDataException($"Date column '{columnName}' was not generated.");
                var nextColumn = text.IndexOf("\r\n\tcolumn ", start + marker.Length, StringComparison.Ordinal);
                if (nextColumn < 0) nextColumn = text.IndexOf("\n\tcolumn ", start + marker.Length, StringComparison.Ordinal);
                var nextChild = text.IndexOf("\r\n\tmeasure ", start + marker.Length, StringComparison.Ordinal);
                if (nextChild < 0) nextChild = text.IndexOf("\n\tmeasure ", start + marker.Length, StringComparison.Ordinal);
                var end = new[] { nextColumn, nextChild, text.Length }.Where(i => i >= 0).Min();
                var block = text[start..end];
                if (block.Contains("\t\tvariation Variation", StringComparison.Ordinal))
                    continue;

                var trimmed = block.TrimEnd('\r', '\n');
                var variation = string.Join(newline, new[]
                {
                    string.Empty,
                    "\t\tvariation Variation",
                    "\t\t\tisDefault",
                    $"\t\t\trelationship: {relationshipId}",
                    $"\t\t\tdefaultHierarchy: {localDateTable}.'Date Hierarchy'"
                });
                text = text[..start] + trimmed + variation + text[end..];
                logger?.Invoke($"SEMANTIC-MODEL-PATCH|DateVariation|{FactTable}.{columnName}|{localDateTable}|{relationshipId}");
            }

            File.WriteAllText(factPath, text, new UTF8Encoding(false));
        }

        private static string MapTmdlDataType(string? dataType) => (dataType ?? "Text").Trim() switch
        {
            "Boolean" => "boolean", "Int64" => "int64", "Double" => "double", "Decimal" => "decimal",
            "DateTime" => "dateTime", "Date" => "date", "Text" => "string", _ => "string"
        };

        private static bool IsNumericColumn(BuiltColumn column) =>
            string.Equals(column.DataType, "Int64", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(column.DataType, "Decimal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(column.DataType, "Double", StringComparison.OrdinalIgnoreCase);

        private static string SanitizeObjectName(string value) =>
            string.IsNullOrWhiteSpace(value) ? "ModelObject" :
            value.Contains(' ') || value.Contains('\\') || value.Contains('/') ? $"'{EscapeSingleQuotes(value)}'" : value;

        private static void PatchMeasures(string factPath, string definitionsPath, Action<string>? logger)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(definitionsPath));
            if (!document.RootElement.TryGetProperty("measures", out var measures) || measures.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("MeasureDefinitions.json must contain a 'measures' array.");

            var text = File.ReadAllText(factPath, Encoding.UTF8);
            var measureRegex = new Regex(
                "(?m)^[ \\t]*measure[ \\t]+(?:'(?<qname>[^']+)'|(?<name>[^=\\r\\n]+?))[ \\t]*=[ \\t]*(?<expr>.*)$",
                RegexOptions.CultureInvariant);

            var existing = new Dictionary<string, MeasureSpan>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in measureRegex.Matches(text))
            {
                var name = match.Groups["qname"].Success
                    ? match.Groups["qname"].Value
                    : match.Groups["name"].Value.Trim();
                existing[name] = new MeasureSpan(
                    match.Groups["expr"].Index,
                    match.Groups["expr"].Index + match.Groups["expr"].Length,
                    match.Groups["expr"].Value.Trim());
            }

            var replacements = new List<(int Start, int End, string Expression, string Name)>();
            var additions = new StringBuilder();
            var measureCount = existing.Count;

            foreach (var measure in measures.EnumerateArray())
            {
                var table = GetString(measure, "Table");
                var name = GetString(measure, "Name");
                if (!string.Equals(table, FactTable, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(name))
                    continue;

                var expression = GetString(measure, "Expression");
                if (string.IsNullOrWhiteSpace(expression))
                    throw new InvalidDataException($"Measure '{name}' has no expression.");
                expression = expression.Trim();

                if (existing.TryGetValue(name, out var current))
                {
                    if (!ExpressionsEqual(current.Expression, expression))
                    {
                        replacements.Add((current.Start, current.End, expression, name));
                        logger?.Invoke($"SEMANTIC-MODEL-PATCH|Updated measure|{name}|ExpressionChanged");
                    }
                    else
                    {
                        logger?.Invoke($"SEMANTIC-MODEL-PATCH|Measure|{name}|AlreadyPresent");
                    }
                    continue;
                }

                additions.AppendLine();
                additions.AppendLine($"\tmeasure '{EscapeSingleQuotes(name)}' = {expression}");
                var format = GetString(measure, "Format");
                var folder = GetString(measure, "Folder");
                if (!string.IsNullOrWhiteSpace(format)) additions.AppendLine($"\t\tformatString: {format}");
                if (!string.IsNullOrWhiteSpace(folder)) additions.AppendLine($"\t\tdisplayFolder: {folder}");
                if (measure.TryGetProperty("Hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True && hidden.GetBoolean()) additions.AppendLine("\t\tisHidden");
                if (measure.TryGetProperty("KPI", out var kpi) && kpi.ValueKind == JsonValueKind.True && kpi.GetBoolean()) additions.AppendLine("\t\tannotation KPI = True");
                if (measure.TryGetProperty("DisplayOrder", out var order) && order.ValueKind == JsonValueKind.Number) additions.AppendLine($"\t\tannotation DisplayOrder = {order.GetInt32()}");
                additions.AppendLine();
                measureCount++;
                logger?.Invoke($"SEMANTIC-MODEL-PATCH|Added measure|{name}");
            }

            // Apply expression replacements from the end of the file backwards so spans remain valid.
            foreach (var replacement in replacements.OrderByDescending(r => r.Start))
                text = text[..replacement.Start] + replacement.Expression + text[replacement.End..];

            if (additions.Length > 0)
            {
                var insertionIndex = text.IndexOf("\tpartition ", StringComparison.Ordinal);
                if (insertionIndex < 0) insertionIndex = text.Length;
                text = text.Insert(insertionIndex, additions.ToString());
            }

            File.WriteAllText(factPath, text, new UTF8Encoding(false));
            logger?.Invoke($"SEMANTIC-MODEL-PATCH|Measures|InlineFactCount={measureCount}");
        }

        private readonly record struct MeasureSpan(int Start, int End, string Expression);

        private static bool ExpressionsEqual(string left, string right)
        {
            static string Normalize(string value) => Regex.Replace(value.Trim(), "\\s+", " ");
            return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
        }

        private static void EnsureFactPartition(string factPath, Action<string>? logger)
        {
            var text = File.ReadAllText(factPath, Encoding.UTF8);
            if (text.Contains("partition Fact_Pipeline_SampleData = m", StringComparison.Ordinal))
            {
                if (!text.Contains("File.Contents(DataFolder & \"\\Fact_Pipeline_SampleData.csv\")", StringComparison.Ordinal)) throw new InvalidDataException("Fact partition exists but does not resolve through DataFolder.");
                logger?.Invoke("SEMANTIC-MODEL-PATCH|FactPartition|DataFolder=AlreadyPresent"); return;
            }
            const string partition = "\n\tpartition Fact_Pipeline_SampleData = m\n\t\tmode: import\n\t\tsource =\n\t\t\t\tlet\n\t\t\t\t  Source = Csv.Document(File.Contents(DataFolder & \"\\Fact_Pipeline_SampleData.csv\"), [Delimiter = \",\", Columns = 17, QuoteStyle = QuoteStyle.None]),\n\t\t\t\t  #\"Promoted headers\" = Table.PromoteHeaders(Source, [PromoteAllScalars = true]),\n\t\t\t\t  #\"Changed column type\" = Table.TransformColumnTypes(#\"Promoted headers\", {{\"PipelineID\", Int64.Type}, {\"PipelineName\", type text}, {\"Category\", type text}, {\"Status\", type text}, {\"ScheduledStart\", type datetime}, {\"ActualStart\", type datetime}, {\"ScheduledEnd\", type datetime}, {\"ActualEnd\", type datetime}, {\"SLAHours\", type number}, {\"DurationHours\", type number}, {\"StartDelayMinutes\", Int64.Type}, {\"EndDelayMinutes\", Int64.Type}, {\"SLAStatus\", type text}, {\"Environment\", type text}, {\"Owner\", type text}, {\"Region\", type text}, {\"RetryCount\", Int64.Type}})\n\t\t\t\tin\n\t\t\t\t  #\"Changed column type\"\n\n\tannotation PBI_NavigationStepName = Navigation\n\n\tannotation PBI_ResultType = Table\n";
            File.WriteAllText(factPath, text.TrimEnd('\r', '\n') + partition, new UTF8Encoding(false));
            logger?.Invoke("SEMANTIC-MODEL-PATCH|FactPartition|Added|DataFolder");
        }

        private static void PatchCategoryRelationship(string path, Action<string>? logger)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var expected = $"fromColumn: {FactTable}.{FactColumn}" + Environment.NewLine + $"\ttoColumn: {DimensionTable}.{DimensionColumn}";
            if (text.Contains(expected, StringComparison.Ordinal)) { logger?.Invoke("SEMANTIC-MODEL-PATCH|Relationship|Dim_Category->Fact_Pipeline_SampleData|AlreadyPresent"); return; }
            var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var block = string.Join(newline, new[] { "relationship Category_Fact_Dimension", $"\tfromColumn: {FactTable}.{FactColumn}", $"\ttoColumn: {DimensionTable}.{DimensionColumn}", string.Empty, string.Empty });
            File.WriteAllText(path, text.TrimEnd('\r', '\n') + newline + block, new UTF8Encoding(false));
            logger?.Invoke("SEMANTIC-MODEL-PATCH|Relationship|Dim_Category->Fact_Pipeline_SLA|Added");
        }

        private static void RemoveLegacyMeasuresTable(string tablesPath, string modelPath, Action<string>? logger)
        {
            var measuresPath = Path.Combine(tablesPath, "_Measures.tmdl");
            if (File.Exists(measuresPath)) { File.Delete(measuresPath); logger?.Invoke("SEMANTIC-MODEL-PATCH|Removed legacy _Measures.tmdl"); }
            if (!File.Exists(modelPath)) return;
            var text = File.ReadAllText(modelPath, Encoding.UTF8);
            var filtered = string.Join(Environment.NewLine, text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Where(line => !line.Trim().Equals("ref table _Measures", StringComparison.OrdinalIgnoreCase)));
            if (!string.Equals(text, filtered, StringComparison.Ordinal)) { File.WriteAllText(modelPath, filtered, new UTF8Encoding(false)); logger?.Invoke("SEMANTIC-MODEL-PATCH|Removed _Measures model reference"); }
        }

        private static void RemoveDanglingMeasureTableAnnotation(string modelPath, string expressionsPath, Action<string>? logger)
        {
            if (File.Exists(modelPath))
            {
                var model = File.ReadAllText(modelPath, Encoding.UTF8);
                var filtered = string.Join(Environment.NewLine, model.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Where(line => !line.Trim().Equals("annotation PBI_QueryOrder = [\"Dim_Category\",\"_Measure Table\",\"Fact_Pipeline_SampleData\"]", StringComparison.OrdinalIgnoreCase)));
                if (!string.Equals(model, filtered, StringComparison.Ordinal)) { File.WriteAllText(modelPath, filtered, new UTF8Encoding(false)); logger?.Invoke("SEMANTIC-MODEL-PATCH|Removed dangling _Measure Table query-order annotation"); }
            }
            if (!File.Exists(expressionsPath)) return;
            var expressions = File.ReadAllText(expressionsPath, Encoding.UTF8);
            const string start = "expression '_Measure Table' =";
            var startIndex = expressions.IndexOf(start, StringComparison.Ordinal);
            if (startIndex < 0) return;
            var nextExpression = expressions.IndexOf("expression ", startIndex + start.Length, StringComparison.Ordinal);
            var endIndex = nextExpression >= 0 ? nextExpression : expressions.Length;
            var cleaned = expressions.Remove(startIndex, endIndex - startIndex).TrimStart('\r', '\n');
            File.WriteAllText(expressionsPath, cleaned, new UTF8Encoding(false));
            logger?.Invoke("SEMANTIC-MODEL-PATCH|Removed dangling _Measure Table expression");
        }

        private static string GetString(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

        private static string EscapeSingleQuotes(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    }
}
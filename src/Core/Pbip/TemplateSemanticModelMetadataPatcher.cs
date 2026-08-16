using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    /// <summary>
    /// Applies only the semantic-model metadata that the authoritative template
    /// is currently missing. The template remains the source of truth; this class
    /// never rebuilds tables or report artifacts.
    /// </summary>
    internal static class TemplateSemanticModelMetadataPatcher
    {
        private const string FactTable = "Fact_Pipeline_SampleData";
        private const string DimensionTable = "Dim_Category";
        private const string FactColumn = "Category";
        private const string DimensionColumn = "CategoryName";

        public static void Patch(string semanticModelRootPath, string repositoryRootPath, Action<string>? logger = null)
        {
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

            PatchMeasures(factPath, measureDefinitionsPath, logger);
            PatchCategoryRelationship(relationshipsPath, tables, logger);
            ReconcileRelationships(relationshipsPath, tables, logger);
            RemoveLegacyMeasuresTable(tables, modelPath, logger);
            RemoveDanglingMeasureTableAnnotation(modelPath, expressionsPath, logger);
        }

        private static void PatchMeasures(string factPath, string definitionsPath, Action<string>? logger)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(definitionsPath));
            if (!document.RootElement.TryGetProperty("measures", out var measures) || measures.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("MeasureDefinitions.json must contain a 'measures' array.");

            var text = File.ReadAllText(factPath, Encoding.UTF8);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("measure ", StringComparison.OrdinalIgnoreCase)) continue;
                var name = trimmed[8..].TrimStart();
                var equals = name.IndexOf('=');
                if (equals >= 0) name = name[..equals].Trim();
                if (name.Length >= 2 && name[0] == '\'' && name[^1] == '\'') name = name[1..^1];
                names.Add(name);
            }

            var additions = new StringBuilder();
            foreach (var measure in measures.EnumerateArray())
            {
                var table = GetString(measure, "Table");
                var name = GetString(measure, "Name");
                if (!string.Equals(table, FactTable, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(name)) continue;
                if (names.Contains(name)) continue;

                var expression = GetString(measure, "Expression");
                if (string.IsNullOrWhiteSpace(expression)) throw new InvalidDataException($"Measure '{name}' has no expression.");

                additions.AppendLine();
                additions.AppendLine($"\tmeasure '{EscapeSingleQuotes(name)}' = {expression.Trim()}");
                var format = GetString(measure, "Format");
                var folder = GetString(measure, "Folder");
                if (!string.IsNullOrWhiteSpace(format)) additions.AppendLine($"\t\tformatString: {format}");
                if (!string.IsNullOrWhiteSpace(folder)) additions.AppendLine($"\t\tdisplayFolder: {folder}");
                if (measure.TryGetProperty("Hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True && hidden.GetBoolean()) additions.AppendLine("\t\tisHidden");
                if (measure.TryGetProperty("KPI", out var kpi) && kpi.ValueKind == JsonValueKind.True && kpi.GetBoolean()) additions.AppendLine("\t\tannotation KPI = True");
                if (measure.TryGetProperty("DisplayOrder", out var order) && order.ValueKind == JsonValueKind.Number) additions.AppendLine($"\t\tannotation DisplayOrder = {order.GetInt32()}");
                additions.AppendLine();
                names.Add(name);
                logger?.Invoke($"SEMANTIC-MODEL-PATCH|Added measure|{name}");
            }

            if (additions.Length > 0)
            {
                var partitionIndex = text.IndexOf("\tpartition ", StringComparison.Ordinal);
                if (partitionIndex < 0) throw new InvalidDataException("Fact table has no partition to append measures before.");
                text = text.Insert(partitionIndex, additions.ToString());
                File.WriteAllText(factPath, text, new UTF8Encoding(false));
            }

            logger?.Invoke($"SEMANTIC-MODEL-PATCH|Measures|InlineFactCount={names.Count}");
        }

        private static void PatchCategoryRelationship(string relationshipsPath, string tablesPath, Action<string>? logger)
        {
            var available = ReadTableColumns(tablesPath);
            if (!HasColumn(available, FactTable, FactColumn) || !HasColumn(available, DimensionTable, DimensionColumn))
            {
                logger?.Invoke("SEMANTIC-MODEL-PATCH|Relationship|Dim_Category->Fact_Pipeline_SampleData|SkippedBecauseColumnsAreAbsent");
                return;
            }

            var text = File.ReadAllText(relationshipsPath, Encoding.UTF8);
            var expected = $"fromColumn: {FactTable}.{FactColumn}";
            var target = $"toColumn: {DimensionTable}.{DimensionColumn}";
            if (text.Contains(expected, StringComparison.Ordinal) && text.Contains(target, StringComparison.Ordinal))
            {
                logger?.Invoke("SEMANTIC-MODEL-PATCH|Relationship|Dim_Category->Fact_Pipeline_SampleData|AlreadyPresent");
                return;
            }

            var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var block = string.Join(newline, new[]
            {
                "relationship Category_Fact_Dimension",
                $"\tfromColumn: {FactTable}.{FactColumn}",
                $"\ttoColumn: {DimensionTable}.{DimensionColumn}",
                string.Empty,
                string.Empty
            });
            File.WriteAllText(relationshipsPath, text.TrimEnd('\r', '\n') + newline + block, new UTF8Encoding(false));
            logger?.Invoke("SEMANTIC-MODEL-PATCH|Relationship|Dim_Category->Fact_Pipeline_SampleData|Added");
        }

        private static void ReconcileRelationships(string relationshipsPath, string tablesPath, Action<string>? logger)
        {
            var available = ReadTableColumns(tablesPath);
            var text = File.ReadAllText(relationshipsPath, Encoding.UTF8);
            var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var blocks = Regex.Split(text.Trim(), @"\r?\n\s*\r?\n")
                .Where(block => !string.IsNullOrWhiteSpace(block))
                .ToList();
            var kept = new List<string>();

            foreach (var block in blocks)
            {
                var nameMatch = Regex.Match(block, @"(?m)^relationship\s+(\S+)");
                var fromMatch = Regex.Match(block, @"(?m)^\s*fromColumn:\s*([^\r\n]+)");
                var toMatch = Regex.Match(block, @"(?m)^\s*toColumn:\s*([^\r\n]+)");
                if (!nameMatch.Success || !fromMatch.Success || !toMatch.Success)
                {
                    kept.Add(block);
                    continue;
                }

                var from = ParseEndpoint(fromMatch.Groups[1].Value.Trim());
                var to = ParseEndpoint(toMatch.Groups[1].Value.Trim());
                if (from is null || to is null || !HasColumn(available, from.Value.Table, from.Value.Column) || !HasColumn(available, to.Value.Table, to.Value.Column))
                {
                    logger?.Invoke($"SEMANTIC-MODEL-PATCH|Relationship|RemovedDangling|{nameMatch.Groups[1].Value}|From={fromMatch.Groups[1].Value.Trim()}|To={toMatch.Groups[1].Value.Trim()}");
                    continue;
                }

                kept.Add(block);
            }

            var reconciled = string.Join(newline + newline, kept) + (kept.Count > 0 ? newline + newline : string.Empty);
            if (!string.Equals(text.TrimEnd('\r', '\n') + (kept.Count > 0 ? newline + newline : string.Empty), reconciled, StringComparison.Ordinal))
                File.WriteAllText(relationshipsPath, reconciled, new UTF8Encoding(false));
            else if (!string.Equals(text, reconciled, StringComparison.Ordinal))
                File.WriteAllText(relationshipsPath, reconciled, new UTF8Encoding(false));
        }

        private static Dictionary<string, HashSet<string>> ReadTableColumns(string tablesPath)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(tablesPath)) return result;

            foreach (var file in Directory.GetFiles(tablesPath, "*.tmdl", SearchOption.TopDirectoryOnly))
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                var tableMatch = Regex.Match(text, @"(?m)^table\s+([^\r\n]+)");
                if (!tableMatch.Success) continue;
                var table = UnquoteIdentifier(tableMatch.Groups[1].Value.Trim());
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in Regex.Matches(text, @"(?m)^\s*column\s+([^\r\n]+)"))
                    columns.Add(UnquoteIdentifier(match.Groups[1].Value.Trim()));
                result[table] = columns;
            }

            return result;
        }

        private static bool HasColumn(IReadOnlyDictionary<string, HashSet<string>> tables, string table, string column)
            => tables.TryGetValue(table, out var columns) && columns.Contains(column);

        private static (string Table, string Column)? ParseEndpoint(string endpoint)
        {
            var separator = endpoint.LastIndexOf('.');
            if (separator <= 0 || separator >= endpoint.Length - 1) return null;
            return (UnquoteIdentifier(endpoint[..separator].Trim()), UnquoteIdentifier(endpoint[(separator + 1)..].Trim()));
        }

        private static string UnquoteIdentifier(string value)
        {
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
                return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
            return value;
        }

        private static void RemoveLegacyMeasuresTable(string tablesPath, string modelPath, Action<string>? logger)
        {
            var measuresPath = Path.Combine(tablesPath, "_Measures.tmdl");
            if (File.Exists(measuresPath))
            {
                File.Delete(measuresPath);
                logger?.Invoke("SEMANTIC-MODEL-PATCH|Removed legacy _Measures.tmdl");
            }

            if (!File.Exists(modelPath)) return;
            var text = File.ReadAllText(modelPath, Encoding.UTF8);
            var filtered = string.Join(Environment.NewLine,
                text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Where(line => !line.Trim().Equals("ref table _Measures", StringComparison.OrdinalIgnoreCase)));
            if (!string.Equals(text, filtered, StringComparison.Ordinal))
            {
                File.WriteAllText(modelPath, filtered, new UTF8Encoding(false));
                logger?.Invoke("SEMANTIC-MODEL-PATCH|Removed _Measures model reference");
            }
        }

        private static void RemoveDanglingMeasureTableAnnotation(string modelPath, string expressionsPath, Action<string>? logger)
        {
            if (File.Exists(modelPath))
            {
                var model = File.ReadAllText(modelPath, Encoding.UTF8);
                var filtered = string.Join(Environment.NewLine,
                    model.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                        .Where(line => !line.Trim().Equals("annotation PBI_QueryOrder = [\"Dim_Category\",\"_Measure Table\",\"Fact_Pipeline_SampleData\"]", StringComparison.OrdinalIgnoreCase)));
                if (!string.Equals(model, filtered, StringComparison.Ordinal))
                {
                    File.WriteAllText(modelPath, filtered, new UTF8Encoding(false));
                    logger?.Invoke("SEMANTIC-MODEL-PATCH|Removed dangling _Measure Table query-order annotation");
                }
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

        private static string GetString(JsonElement element, string property)
            => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

        private static string EscapeSingleQuotes(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    }
}

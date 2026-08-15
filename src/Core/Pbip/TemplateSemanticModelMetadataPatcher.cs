using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

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
            var measureDefinitionsPath = Path.Combine(repositoryRootPath, "scripts", "metadata", "MeasureDefinitions.json");

            if (!File.Exists(factPath)) throw new FileNotFoundException("Fact table template is missing.", factPath);
            if (!File.Exists(relationshipsPath)) throw new FileNotFoundException("Template is missing relationships.tmdl.", relationshipsPath);
            if (!File.Exists(measureDefinitionsPath)) throw new FileNotFoundException("MeasureDefinitions.json is missing.", measureDefinitionsPath);

            PatchMeasures(factPath, measureDefinitionsPath, logger);
            PatchCategoryRelationship(relationshipsPath, logger);
            RemoveLegacyMeasuresTable(tables, modelPath, logger);
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

        private static void PatchCategoryRelationship(string path, Action<string>? logger)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var expected = $"fromColumn: {FactTable}.{FactColumn}" + Environment.NewLine + $"\ttoColumn: {DimensionTable}.{DimensionColumn}";
            if (text.Contains(expected, StringComparison.Ordinal))
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
            File.WriteAllText(path, text.TrimEnd('\r', '\n') + newline + block, new UTF8Encoding(false));
            logger?.Invoke("SEMANTIC-MODEL-PATCH|Relationship|Dim_Category->Fact_Pipeline_SampleData|Added");
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

        private static string GetString(JsonElement element, string property)
            => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

        private static string EscapeSingleQuotes(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    }
}

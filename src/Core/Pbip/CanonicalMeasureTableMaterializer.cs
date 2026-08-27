using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    /// <summary>
    /// Materializes metadata-defined measures into the canonical _Measure Table.
    /// MeasureDefinitions.json is the source of truth; measures are never left inline
    /// on Fact_Pipeline_SampleData and the legacy _Measures artifact is rejected.
    /// </summary>
    internal static class CanonicalMeasureTableMaterializer
    {
        private const string FactTable = "Fact_Pipeline_SampleData";
        private const string CanonicalTable = "_Measure Table";
        private const string LegacyFile = "_Measures.tmdl";

        public static void Materialize(string semanticModelRootPath, string repositoryRootPath, Action<string>? logger = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRootPath);

            var tablesPath = Path.Combine(semanticModelRootPath, "definition", "tables");
            var factPath = Path.Combine(tablesPath, $"{FactTable}.tmdl");
            var modelPath = Path.Combine(semanticModelRootPath, "definition", "model.tmdl");
            var definitionsPath = Path.Combine(repositoryRootPath, "scripts", "metadata", "MeasureDefinitions.json");
            var canonicalPath = Path.Combine(tablesPath, $"{CanonicalTable}.tmdl");
            var legacyPath = Path.Combine(tablesPath, LegacyFile);

            if (!File.Exists(factPath)) throw new FileNotFoundException("Fact table is missing.", factPath);
            if (!File.Exists(modelPath)) throw new FileNotFoundException("Semantic model model.tmdl is missing.", modelPath);
            if (!File.Exists(definitionsPath)) throw new FileNotFoundException("MeasureDefinitions.json is missing.", definitionsPath);

            var document = JsonDocument.Parse(File.ReadAllText(definitionsPath));
            using (document)
            {
                if (!document.RootElement.TryGetProperty("measures", out var measures) || measures.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("MeasureDefinitions.json must contain a 'measures' array.");

                var builder = new StringBuilder();
                builder.AppendLine($"table '{CanonicalTable}'");
                builder.AppendLine();

                var count = 0;
                foreach (var measure in measures.EnumerateArray())
                {
                    var table = GetString(measure, "Table");
                    var name = GetString(measure, "Name");
                    var expression = GetString(measure, "Expression");
                    if (!string.Equals(table, FactTable, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(name))
                        continue;
                    if (string.IsNullOrWhiteSpace(expression))
                        throw new InvalidDataException($"Measure '{name}' has no expression.");

                    builder.AppendLine($"\tmeasure '{Escape(name)}' = {NormalizeExpression(expression)}");
                    var format = GetString(measure, "Format");
                    var folder = GetString(measure, "Folder");
                    if (!string.IsNullOrWhiteSpace(format)) builder.AppendLine($"\t\tformatString: '{Escape(format)}'");
                    if (!string.IsNullOrWhiteSpace(folder)) builder.AppendLine($"\t\tdisplayFolder: '{Escape(folder)}'");
                    if (measure.TryGetProperty("Hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True && hidden.GetBoolean()) builder.AppendLine("\t\tisHidden: true");
                    builder.AppendLine();
                    count++;
                }

                if (count == 0) throw new InvalidDataException("MeasureDefinitions.json contains no Fact_Pipeline_SampleData measures.");

                builder.AppendLine($"\tpartition '{CanonicalTable}' = m");
                builder.AppendLine("\t\tmode: import");
                builder.AppendLine("\t\tsource =");
                builder.AppendLine("\t\t\t\tlet");
                builder.AppendLine("\t\t\t\t    Source = #table({}, {})");
                builder.AppendLine("\t\t\t\tin");
                builder.AppendLine("\t\t\t\t    Source");

                File.WriteAllText(canonicalPath, builder.ToString(), new UTF8Encoding(false));
                logger?.Invoke($"SEMANTIC-MODEL-PATCH|CanonicalMeasureTable|Path={canonicalPath}|Measures={count}");
            }

            RemoveInlineMeasures(factPath, logger);
            if (File.Exists(legacyPath)) File.Delete(legacyPath);

            var modelText = File.ReadAllText(modelPath, Encoding.UTF8);
            modelText = Regex.Replace(modelText, @"(?m)^ref table _Measures\s*\r?\n", string.Empty);
            if (!Regex.IsMatch(modelText, @"(?m)^ref table '_Measure Table'\s*$"))
            {
                var marker = "\tref cultureInfo en-US";
                var insert = $"ref table '{CanonicalTable}'\r\n\r\n";
                var markerIndex = modelText.IndexOf(marker, StringComparison.Ordinal);
                if (markerIndex >= 0) modelText = modelText.Insert(markerIndex, insert);
                else modelText += "\r\nref table '_Measure Table'\r\n";
            }
            File.WriteAllText(modelPath, modelText, new UTF8Encoding(false));
            logger?.Invoke("SEMANTIC-MODEL-PATCH|CanonicalMeasureTable|Legacy=_Measures rejected|InlineFactMeasures=0");
        }

        private static void RemoveInlineMeasures(string factPath, Action<string>? logger)
        {
            var text = File.ReadAllText(factPath, Encoding.UTF8);
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var output = new StringBuilder();
            var skipping = false;
            var removed = 0;

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                var startsChild = trimmed.StartsWith("column ", StringComparison.OrdinalIgnoreCase) ||
                                  trimmed.StartsWith("measure ", StringComparison.OrdinalIgnoreCase) ||
                                  trimmed.StartsWith("partition ", StringComparison.OrdinalIgnoreCase);
                if (trimmed.StartsWith("measure ", StringComparison.OrdinalIgnoreCase))
                {
                    skipping = true;
                    removed++;
                    continue;
                }
                if (skipping && startsChild) skipping = false;
                if (!skipping) output.AppendLine(line);
            }

            File.WriteAllText(factPath, output.ToString().Replace("\n", Environment.NewLine), new UTF8Encoding(false));
            logger?.Invoke($"SEMANTIC-MODEL-PATCH|CanonicalMeasureTable|RemovedInlineMeasures={removed}");
        }

        private static string GetString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;

        private static string NormalizeExpression(string expression) => Regex.Replace(expression.Trim(), @"\s*\r?\n\s*", " ");
        private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    }
}

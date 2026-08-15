using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    internal static class PbirVisualContainerNormalizer
    {
        private const string LegacyMeasuresTable = "_Measures";
        private const string GeneratedMeasuresTable = "Fact_Pipeline_SampleData";
        private static readonly IReadOnlyDictionary<string, string> MeasureNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Active Pipelines"] = "Active Pipelines", ["Successful Runs"] = "Successful Runs", ["Failed Runs"] = "Failed Runs",
            ["Running Pipelines"] = "Running Pipelines", ["Queued Pipelines"] = "Queued Pipelines", ["Success Rate %"] = "Success Rate %",
            ["Failure Rate %"] = "Failure Rate %", ["SLA Breach %"] = "SLA Breach %", ["Average Runtime"] = "Average Runtime",
            ["Total Runtime"] = "Total Runtime", ["Maximum Runtime"] = "Maximum Runtime", ["Minimum Runtime"] = "Minimum Runtime",
            ["Total Runs"] = "Total Runs", ["Breached Count"] = "Breached Count", ["SLA Compliance %"] = "SLA Compliance %",
            ["Timeline Base"] = "Timeline Base", ["Floating Bar Duration"] = "Floating Bar Duration", ["Floating Bar Status"] = "Floating Bar Status",
            ["SLA Breach Color"] = "SLA Breach Color", ["Average Start Delay"] = "Average Start Delay", ["Average End Delay"] = "Average End Delay",
            ["Success Rate KPI"] = "Success Rate KPI", ["Last Refresh"] = "Last Refresh"
        };

        public static void NormalizeReport(string reportRootPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reportRootPath);
            var definitionPath = Path.Combine(reportRootPath, "definition", "pages");
            if (!Directory.Exists(definitionPath)) return;
            var semanticModelRoot = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(reportRootPath)) ?? string.Empty, Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(reportRootPath)) + ".SemanticModel");
            var measures = Directory.Exists(semanticModelRoot) ? ReadGeneratedMeasures(semanticModelRoot) : new HashSet<(string Table, string Measure)>();
            var errors = new List<string>();
            foreach (var visualPath in Directory.EnumerateFiles(definitionPath, "visual.json", SearchOption.AllDirectories)) NormalizeVisual(visualPath, measures, errors);
            if (errors.Count > 0) throw new InvalidOperationException("PBIR measure-reference validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => " - " + e)));
        }

        private static void NormalizeVisual(string visualPath, HashSet<(string Table, string Measure)> measures, List<string> errors)
        {
            var originalBytes = File.ReadAllBytes(visualPath);
            var hadUtf8Bom = HasUtf8Bom(originalBytes);
            var jsonBytes = hadUtf8Bom ? originalBytes[3..] : originalBytes;
            var json = new UTF8Encoding(false, true).GetString(jsonBytes);
            var root = JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException($"PBIR visual '{visualPath}' must contain a JSON object.");
            var changed = NormalizeMeasureSourceRefs(root, visualPath, measures, errors);
            ValidateVisual(root, visualPath);
            if (changed) File.WriteAllText(visualPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            else if (hadUtf8Bom) File.WriteAllBytes(visualPath, jsonBytes);
        }

        private static bool HasUtf8Bom(byte[] bytes) => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        private static bool NormalizeMeasureSourceRefs(JsonNode node, string visualPath, HashSet<(string Table, string Measure)> measures, List<string> errors)
        {
            var changed = false;
            if (node is JsonObject obj)
            {
                if (obj["Measure"] is JsonObject measure && measure["Expression"] is JsonObject expression && expression["SourceRef"] is JsonObject sourceRef)
                {
                    var entity = sourceRef["Entity"]?.GetValue<string>();
                    var property = measure["Property"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(entity) && !string.IsNullOrWhiteSpace(property))
                    {
                        var mapped = MeasureNameMap.TryGetValue(property!, out var mappedName) ? mappedName : property!;
                        if (string.Equals(entity, LegacyMeasuresTable, StringComparison.OrdinalIgnoreCase))
                        {
                            sourceRef["Entity"] = GeneratedMeasuresTable;
                            measure["Property"] = mapped;
                            changed = true;
                        }
                        else if (string.Equals(entity, GeneratedMeasuresTable, StringComparison.OrdinalIgnoreCase))
                        {
                            measure["Property"] = mapped;
                            changed |= !string.Equals(property, mapped, StringComparison.Ordinal);
                        }
                        if (string.Equals(sourceRef["Entity"]?.GetValue<string>(), GeneratedMeasuresTable, StringComparison.OrdinalIgnoreCase))
                            ValidateMeasureReference(visualPath, GeneratedMeasuresTable, mapped, measures, errors);
                    }
                }

                foreach (var property in obj.ToList())
                {
                    var value = property.Value;
                    if (value is null) continue;
                    if (property.Key is "queryRef" or "nativeQueryRef" or "metadata")
                    {
                        var text = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
                        if (!string.IsNullOrWhiteSpace(text) && text.StartsWith(LegacyMeasuresTable + ".", StringComparison.OrdinalIgnoreCase))
                        {
                            var oldName = text[(LegacyMeasuresTable.Length + 1)..];
                            var mapped = MeasureNameMap.TryGetValue(oldName, out var mappedName) ? mappedName : oldName;
                            obj[property.Key] = $"{GeneratedMeasuresTable}.{mapped}";
                            changed = true;
                            continue;
                        }
                    }
                    if (NormalizeMeasureSourceRefs(value, visualPath, measures, errors)) changed = true;
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array) if (item is not null && NormalizeMeasureSourceRefs(item, visualPath, measures, errors)) changed = true;
            }
            return changed;
        }

        private static void ValidateMeasureReference(string visualPath, string entity, string property, HashSet<(string Table, string Measure)> measures, List<string> errors)
        {
            if (!measures.Contains((entity, property))) errors.Add($"{visualPath}: Measure reference '{entity}.{property}' does not resolve to a generated measure.");
        }

        private static HashSet<(string Table, string Measure)> ReadGeneratedMeasures(string semanticModelRoot)
        {
            var result = new HashSet<(string Table, string Measure)>();
            string? currentTable = null;
            var tablePattern = new Regex(@"^\s*table\s+(.+?)\s*$", RegexOptions.Compiled);
            var measurePattern = new Regex(@"^\s*measure\s+'((?:''|[^'])+)'\s*=", RegexOptions.Compiled);
            foreach (var file in Directory.GetFiles(semanticModelRoot, "*.tmdl", SearchOption.AllDirectories)) foreach (var line in File.ReadLines(file))
            {
                var table = tablePattern.Match(line);
                if (table.Success) { currentTable = table.Groups[1].Value.Trim().Trim('\'').Replace("''", "'", StringComparison.Ordinal); continue; }
                var measure = measurePattern.Match(line);
                if (measure.Success && !string.IsNullOrWhiteSpace(currentTable)) result.Add((currentTable!, measure.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal)));
            }
            return result;
        }

        private static void ValidateVisual(JsonObject root, string visualPath)
        {
            if (root["visual"] is not JsonObject visual || visual["visualContainerObjects"] is not JsonObject containerObjects) return;
            if (containerObjects["title"] is JsonNode title && title is not JsonArray) throw new InvalidOperationException($"PBIR visual '{visualPath}' has an invalid visualContainerObjects.title value. The generator must serialize title as a JSON array.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PowerBiPipelineSlaTemplate.Core.Pbip;

internal static class PbirMeasureReferenceMigrator
{
    private const string LegacyMeasuresTable = "_Measures";
    private const string GeneratedMeasuresTable = "_Measure Table";
    private const string PreviousGeneratedMeasuresTable = "Fact_Pipeline_SampleData";

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

    public static void MigrateAndValidate(string reportRoot, string semanticModelRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportRoot); ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRoot);
        var measures = ReadGeneratedMeasures(semanticModelRoot);
        if (measures.Count == 0) throw new InvalidOperationException($"No generated measures were found under '{semanticModelRoot}'.");
        var visualRoot = ResolveVisualRoot(reportRoot); var migrated = 0; var visualCount = 0; var errors = new List<string>();
        foreach (var visualPath in Directory.GetFiles(visualRoot, "visual.json", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            visualCount++;
            JsonNode root;
            try { root = JsonNode.Parse(File.ReadAllText(visualPath)) ?? throw new InvalidOperationException("JSON document is null."); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { errors.Add($"{visualPath}: invalid JSON: {ex.Message}"); continue; }
            var changed = MigrateNode(root, visualPath, errors, measures);
            if (changed) { File.WriteAllText(visualPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false)); migrated++; }
        }
        if (errors.Count > 0) throw new InvalidOperationException("PBIR measure-reference validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(x => " - " + x)));
        Console.WriteLine($"PBIR measure-reference validation passed: {visualCount} visual(s), {migrated} visual(s) migrated to '{GeneratedMeasuresTable}'.");
    }

    private static bool MigrateNode(JsonNode node, string visualPath, List<string> errors, HashSet<(string Table, string Measure)> measures)
    {
        var changed = false;
        if (node is JsonObject obj)
        {
            if (obj["Measure"] is JsonObject measure && measure["Expression"] is JsonObject expression && expression["SourceRef"] is JsonObject sourceRef)
            {
                var entity = sourceRef["Entity"]?.GetValue<string>(); var property = measure["Property"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(entity) && !string.IsNullOrWhiteSpace(property) &&
                    TryResolveKnownMeasure(entity!, property!, measures, out var canonicalName))
                {
                    sourceRef["Entity"] = GeneratedMeasuresTable;
                    measure["Property"] = canonicalName;
                    changed = !string.Equals(entity, GeneratedMeasuresTable, StringComparison.OrdinalIgnoreCase) || !string.Equals(property, canonicalName, StringComparison.Ordinal);
                    ValidateMeasureReference(visualPath, GeneratedMeasuresTable, canonicalName, measures, errors);
                }
            }

            foreach (var property in obj.ToList())
            {
                var value = property.Value; if (value is null) continue;
                if (property.Key is "queryRef" or "nativeQueryRef" or "metadata")
                {
                    var text = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
                    if (!string.IsNullOrWhiteSpace(text) && TryParseMeasureReference(text!, out var table, out var name) &&
                        TryResolveKnownMeasure(table, name, measures, out var canonicalName))
                    {
                        var canonicalReference = $"{GeneratedMeasuresTable}.{canonicalName}";
                        if (!string.Equals(text, canonicalReference, StringComparison.Ordinal))
                        {
                            obj[property.Key] = canonicalReference;
                            changed = true;
                        }
                        ValidateMeasureReference(visualPath, GeneratedMeasuresTable, canonicalName, measures, errors);
                        continue;
                    }
                }
                if (MigrateNode(value, visualPath, errors, measures)) changed = true;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array) if (item is not null && MigrateNode(item, visualPath, errors, measures)) changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Resolves only an exact known measure. A table alias by itself is not sufficient:
    /// ordinary Fact_Pipeline_SampleData columns must remain byte-for-byte semantically unchanged.
    /// </summary>
    private static bool TryResolveKnownMeasure(string table, string name, HashSet<(string Table, string Measure)> measures, out string canonicalName)
    {
        canonicalName = ResolveMeasureName(name);
        if (!IsMeasureTableAlias(table)) return false;
        return measures.Contains((GeneratedMeasuresTable, canonicalName));
    }

    private static bool IsMeasureTableAlias(string entity) =>
        string.Equals(entity, LegacyMeasuresTable, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entity, PreviousGeneratedMeasuresTable, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entity, GeneratedMeasuresTable, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseMeasureReference(string value, out string table, out string measure)
    {
        table = string.Empty; measure = string.Empty;
        var separator = value.IndexOf('.');
        if (separator <= 0 || separator == value.Length - 1) return false;
        table = value[..separator]; measure = value[(separator + 1)..];
        return true;
    }

    private static string ResolveMeasureName(string name) => MeasureNameMap.TryGetValue(name, out var mapped) ? mapped : name;
    private static void ValidateMeasureReference(string visualPath, string entity, string property, HashSet<(string Table, string Measure)> measures, List<string> errors) { if (!measures.Contains((entity, property))) errors.Add($"{visualPath}: Measure reference '{entity}.{property}' does not resolve to a generated measure."); }

    private static HashSet<(string Table, string Measure)> ReadGeneratedMeasures(string semanticModelRoot)
    {
        var result = new HashSet<(string Table, string Measure)>(); string? currentTable = null;
        var tablePattern = new Regex(@"^\s*table\s+(.+?)\s*$", RegexOptions.Compiled); var measurePattern = new Regex(@"^\s*measure\s+'((?:''|[^'])+)'\s*=", RegexOptions.Compiled);
        foreach (var file in Directory.GetFiles(semanticModelRoot, "*.tmdl", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) foreach (var line in File.ReadLines(file))
        {
            var table = tablePattern.Match(line); if (table.Success) { currentTable = table.Groups[1].Value.Trim().Trim('\'').Replace("''", "'", StringComparison.Ordinal); continue; }
            var measure = measurePattern.Match(line); if (measure.Success && !string.IsNullOrWhiteSpace(currentTable)) result.Add((currentTable!, measure.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal)));
        }
        return result;
    }

    private static string ResolveVisualRoot(string reportRoot)
    {
        var definition = Path.Combine(reportRoot, "definition", "pages"); if (Directory.Exists(definition)) return definition;
        var legacy = Path.Combine(reportRoot, "pages"); if (Directory.Exists(legacy)) return legacy;
        throw new DirectoryNotFoundException($"Unable to locate PBIR pages under '{reportRoot}'.");
    }
}

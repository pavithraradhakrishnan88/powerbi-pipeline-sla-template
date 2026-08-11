using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    internal static class PbirVisualContainerNormalizer
    {
        public static void NormalizeReport(string reportRootPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reportRootPath);
            var definitionPath = Path.Combine(reportRootPath, "definition", "pages");
            if (!Directory.Exists(definitionPath)) return;

            foreach (var visualPath in Directory.EnumerateFiles(definitionPath, "visual.json", SearchOption.AllDirectories))
            {
                NormalizeVisual(visualPath);
            }
        }

        private static void NormalizeVisual(string visualPath)
        {
            var root = JsonNode.Parse(File.ReadAllText(visualPath)) as JsonObject
                ?? throw new InvalidOperationException($"PBIR visual '{visualPath}' must contain a JSON object.");

            var changed = NormalizeMeasureSourceRefs(root);
            ValidateVisual(root, visualPath);

            if (changed)
            {
                File.WriteAllText(visualPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        private static bool NormalizeMeasureSourceRefs(JsonNode node)
        {
            var changed = false;

            if (node is JsonObject obj)
            {
                if (obj["Measure"] is JsonObject measure &&
                    measure["Expression"] is JsonObject expression &&
                    expression["SourceRef"] is JsonObject sourceRef &&
                    sourceRef["Entity"] is JsonValue entityValue &&
                    entityValue.TryGetValue<string>(out var entity) &&
                    string.Equals(entity, "Fact_Pipeline_SampleData", StringComparison.Ordinal))
                {
                    sourceRef["Entity"] = "_Measures";
                    changed = true;
                }

                foreach (var property in obj)
                {
                    if (property.Value is not null && NormalizeMeasureSourceRefs(property.Value)) changed = true;
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is not null && NormalizeMeasureSourceRefs(item)) changed = true;
                }
            }

            return changed;
        }

        private static void ValidateVisual(JsonObject root, string visualPath)
        {
            if (root["visual"] is not JsonObject visual ||
                visual["visualContainerObjects"] is not JsonObject containerObjects)
            {
                return;
            }

            if (containerObjects["title"] is JsonNode title && title is not JsonArray)
            {
                throw new InvalidOperationException(
                    $"PBIR visual '{visualPath}' has an invalid visualContainerObjects.title value. The generator must serialize title as a JSON array.");
            }
        }
    }
}
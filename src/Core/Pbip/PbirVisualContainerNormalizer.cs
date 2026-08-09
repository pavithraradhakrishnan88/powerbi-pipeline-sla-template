using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    internal static class PbirVisualContainerNormalizer
    {
        // Kept as the existing writer hook, but validation is now non-mutating.
        // visualContainerObjects.title must already be serialized as a JsonArray by the template/generator.
        public static void NormalizeReport(string reportRootPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reportRootPath);
            var definitionPath = Path.Combine(reportRootPath, "definition", "pages");
            if (!Directory.Exists(definitionPath)) return;
            foreach (var visualPath in Directory.EnumerateFiles(definitionPath, "visual.json", SearchOption.AllDirectories)) ValidateVisual(visualPath);
        }

        private static void ValidateVisual(string visualPath)
        {
            var root = JsonNode.Parse(File.ReadAllText(visualPath)) as JsonObject
                ?? throw new InvalidOperationException($"PBIR visual '{visualPath}' must contain a JSON object.");

            if (root["visualContainerObjects"] is not JsonObject containerObjects)
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

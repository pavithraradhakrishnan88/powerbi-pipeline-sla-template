using System;
using System.IO;
using System.Text.Json.Nodes;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    internal static class PbirVisualContainerNormalizer
    {
        // Kept as the existing writer hook, but validation is non-mutating.
        // PBIR visualContainerObjects belongs inside visual and title must be a JSON array.
        public static void NormalizeReport(string reportRootPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reportRootPath);
            var definitionPath = Path.Combine(reportRootPath, "definition", "pages");
            if (!Directory.Exists(definitionPath)) return;

            foreach (var visualPath in Directory.EnumerateFiles(definitionPath, "visual.json", SearchOption.AllDirectories))
            {
                ValidateVisual(visualPath);
            }
        }

        private static void ValidateVisual(string visualPath)
        {
            var root = JsonNode.Parse(File.ReadAllText(visualPath)) as JsonObject
                ?? throw new InvalidOperationException($"PBIR visual '{visualPath}' must contain a JSON object.");

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

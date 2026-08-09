using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    internal static class PbirVisualContainerNormalizer
    {
        // PBIR title metadata is normalized during report generation, never by patching BuildResult.
        public static void NormalizeReport(string reportRootPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reportRootPath);
            var definitionPath = Path.Combine(reportRootPath, "definition", "pages");
            if (!Directory.Exists(definitionPath)) return;
            foreach (var visualPath in Directory.EnumerateFiles(definitionPath, "visual.json", SearchOption.AllDirectories)) NormalizeVisual(visualPath);
        }

        private static void NormalizeVisual(string visualPath)
        {
            var root = JsonNode.Parse(File.ReadAllText(visualPath)) as JsonObject
                ?? throw new InvalidOperationException($"PBIR visual '{visualPath}' must contain a JSON object.");
            if (root["visual"] is not JsonObject visual) return;

            if (visual["visualContainerObjects"] is JsonNode nestedContainerObjects)
            {
                visual.Remove("visualContainerObjects");
                if (root["visualContainerObjects"] is null) root["visualContainerObjects"] = nestedContainerObjects;
            }

            if (root["visualContainerObjects"] is JsonObject containerObjects && containerObjects["title"] is JsonNode title)
            {
                if (title is JsonObject titleObject) containerObjects["title"] = new JsonArray(titleObject.DeepClone());
                else if (title is not JsonArray) throw new InvalidOperationException($"PBIR visual '{visualPath}' has an invalid visualContainerObjects.title value. Expected an object or array of objects.");
            }

            File.WriteAllText(visualPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

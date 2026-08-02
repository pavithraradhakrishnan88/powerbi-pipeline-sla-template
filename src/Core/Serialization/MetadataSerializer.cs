using System.IO;
using System.Text.Json;
using PowerBiPipelineSlaTemplate.Core.Models;

namespace PowerBiPipelineSlaTemplate.Core.Serialization
{
    public static class MetadataSerializer
    {
        public static string ToJson(MetadataDocument metadata, bool indented = true)
        {
            return System.Text.Json.JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = indented,
                PropertyNamingPolicy = null
            });
        }

        public static void Save(MetadataDocument metadata, string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = System.Text.Json.JsonSerializer.Serialize(metadata, options);

            File.WriteAllText(filePath, json);
        }
    }
}

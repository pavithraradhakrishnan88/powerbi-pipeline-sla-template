using System.Text.Json;

namespace PowerBiPipelineSlaTemplate.Core.Serialization
{
    public static class JsonSerializer
    {
        public static string ToJson(object value, bool indented = true)
        {
            return System.Text.Json.JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                WriteIndented = indented,
                PropertyNamingPolicy = null
            });
        }
    }
}

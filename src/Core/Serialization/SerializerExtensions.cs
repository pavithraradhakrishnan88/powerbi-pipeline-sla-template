using System;
using System.Text.Json;

namespace PowerBiPipelineSlaTemplate.Core.Serialization
{
    public static class SerializerExtensions
    {
        public static string ToJson<T>(this T value, bool indented = true)
        {
            return System.Text.Json.JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                WriteIndented = indented,
                PropertyNamingPolicy = null
            });
        }

        public static T? FromJson<T>(this string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            return System.Text.Json.JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
    }
}

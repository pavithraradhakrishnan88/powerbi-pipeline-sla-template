using System;
using System.Globalization;

namespace PowerBiPipelineSlaTemplate.Core.Utilities
{
    public static class TypeInference
    {
        public static string InferType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "string";
            }

            if (bool.TryParse(value, out _))
            {
                return "boolean";
            }

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return "integer";
            }

            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                return "decimal";
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out _))
            {
                return "datetime";
            }

            return "string";
        }

        public static string InferTypeFromClrType(Type? type)
        {
            if (type == null)
            {
                return "string";
            }

            if (type == typeof(bool))
            {
                return "boolean";
            }

            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            {
                return "integer";
            }

            if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
            {
                return "decimal";
            }

            if (type == typeof(DateTime))
            {
                return "datetime";
            }

            if (type == typeof(Guid))
            {
                return "guid";
            }

            return "string";
        }
    }
}

namespace PowerBiPipelineSlaTemplate.Core.Utilities
{
    public static class DatabaseTypeMapper
    {
        public static string Map(string inferredType)
        {
            return inferredType switch
            {
                "boolean" => "BIT",
                "integer" => "INT",
                "decimal" => "DECIMAL(18,4)",
                "datetime" => "DATETIME2",
                _ => "NVARCHAR"
            };
        }

        public static string MapPowerBiType(string inferredType)
        {
            return inferredType switch
            {
                "boolean" => "Boolean",
                "integer" => "Whole Number",
                "decimal" => "Decimal Number",
                "datetime" => "DateTime",
                _ => "Text"
            };
        }
    }
}

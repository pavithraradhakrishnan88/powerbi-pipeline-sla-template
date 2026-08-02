namespace PowerBiPipelineSlaTemplate.Core.Models
{
    public class ColumnMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "string";
        public bool Nullable { get; set; } = true;
        public int? Length { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsForeignKey { get; set; }
        public string Description { get; set; } = string.Empty;
        public string DatabaseType { get; set; } = string.Empty;
        public string PowerBiType { get; set; } = "Text";
    }
}

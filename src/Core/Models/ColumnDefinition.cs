namespace PowerBiPipelineSlaTemplate.Core.Models
{
    public class ColumnDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "string";
        public bool Nullable { get; set; } = true;
        public int? Length { get; set; }
        public bool IsPrimaryKeyCandidate { get; set; }
        public bool IsForeignKeyCandidate { get; set; }
        public string? SampleValue { get; set; }
        public int DistinctCount { get; set; }
        public string? Minimum { get; set; }
        public string? Maximum { get; set; }
    }
}

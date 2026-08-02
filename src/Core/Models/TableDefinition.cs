using System.Collections.Generic;

namespace PowerBiPipelineSlaTemplate.Core.Models
{
    public class TableDefinition
    {
        public string Name { get; set; } = string.Empty;
        public List<ColumnDefinition> Columns { get; set; } = new();
        public int RowCount { get; set; }
        public string? SampleValue { get; set; }
        public int DistinctCount { get; set; }
        public bool IsFactTable { get; set; }
        public bool IsDimensionTable { get; set; }
    }
}

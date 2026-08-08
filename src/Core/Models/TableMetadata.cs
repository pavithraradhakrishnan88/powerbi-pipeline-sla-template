using System.Collections.Generic;

namespace PowerBiPipelineSlaTemplate.Core.Models
{
    public class TableMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayFolder { get; set; } = string.Empty;
        public bool IsFactTable { get; set; }
        public bool IsDimensionTable { get; set; }
        public int RowCount { get; set; }
        public List<ColumnMetadata> Columns { get; set; } = new();
    }
}

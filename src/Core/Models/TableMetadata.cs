using System.Collections.Generic;

namespace PowerBiPipelineSlaTemplate.Core.Models
{
    public class TableMetadata
    {
        public string Name { get; set; } = string.Empty;
        public List<ColumnMetadata> Columns { get; set; } = new();
    }
}

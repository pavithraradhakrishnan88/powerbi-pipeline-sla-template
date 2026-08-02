using System.Collections.Generic;

namespace PowerBiPipelineSlaTemplate.Core.Models
{
    public class DatabaseSchema
    {
        public List<TableDefinition> Tables { get; set; } = new();
    }
}

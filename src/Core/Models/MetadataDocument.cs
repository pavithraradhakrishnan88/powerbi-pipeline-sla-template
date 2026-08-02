using System.Collections.Generic;

namespace PowerBiPipelineSlaTemplate.Core.Models
{
    public class MetadataDocument
    {
        public List<TableMetadata> Tables { get; set; } = new();
    }
}

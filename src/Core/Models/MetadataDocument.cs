using System.Collections.Generic;

namespace PowerBiPipelineSlaTemplate.Core.Models
{
    public class MetadataDocument
    {
        public ProjectMetadata Project { get; set; } = new();
        public MetadataSummary Summary { get; set; } = new();
        public List<TableMetadata> Tables { get; set; } = new();
    }

    public class ProjectMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Generator { get; set; } = string.Empty;
        public string GeneratedOn { get; set; } = string.Empty;
        public string Repository { get; set; } = string.Empty;
    }

    public class MetadataSummary
    {
        public int TableCount { get; set; }
        public int ColumnCount { get; set; }
        public int RelationshipCount { get; set; }
        public int FactTables { get; set; }
        public int DimensionTables { get; set; }
        public int MeasureCount { get; set; }
        public int RowCount { get; set; }
    }
}

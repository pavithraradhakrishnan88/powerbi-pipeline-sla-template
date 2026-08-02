using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PowerBiPipelineSlaTemplate.Core.Csv;
using PowerBiPipelineSlaTemplate.Core.Excel;
using PowerBiPipelineSlaTemplate.Core.Models;
using PowerBiPipelineSlaTemplate.Core.Serialization;
using PowerBiPipelineSlaTemplate.Core.Sql;

namespace PowerBiPipelineSlaTemplate.Core
{
    public class MetadataExtractor
    {
        public MetadataDocument ExtractFromCsv(string filePath, string? tableName = null)
        {
            return ExtractFromCsvAsync(filePath, tableName).GetAwaiter().GetResult();
        }

        public MetadataDocument ExtractFromExcel(string filePath, string? sheetName = null, string? tableName = null)
        {
            return ExtractFromExcelAsync(filePath, sheetName, tableName).GetAwaiter().GetResult();
        }

        public MetadataDocument ExtractFromSql(string connectionString, string query, string? tableName = null)
        {
            return ExtractFromSqlAsync(connectionString, query, tableName).GetAwaiter().GetResult();
        }

        public Task<MetadataDocument> ExtractFromCsvAsync(string filePath, string? tableName = null, CancellationToken cancellationToken = default)
        {
            var extractor = new CsvMetadataExtractor(filePath, tableName);
            return extractor.ExtractAsync(cancellationToken);
        }

        public Task<MetadataDocument> ExtractFromExcelAsync(string filePath, string? sheetName = null, string? tableName = null, CancellationToken cancellationToken = default)
        {
            var extractor = new ExcelMetadataExtractor(filePath, sheetName, tableName);
            return extractor.ExtractAsync(cancellationToken);
        }

        public Task<MetadataDocument> ExtractFromSqlAsync(string connectionString, string query, string? tableName = null, CancellationToken cancellationToken = default)
        {
            var extractor = new SqlMetadataExtractor(connectionString, query, tableName);
            return extractor.ExtractAsync(cancellationToken);
        }

        public MetadataDocument BuildMetadata(IEnumerable<TableMetadata> tables)
        {
            return new MetadataDocument
            {
                Tables = tables?.Where(table => table != null).Select(table => new TableMetadata
                {
                    Name = table.Name,
                    Columns = table.Columns?.Where(column => column != null).Select(column => new ColumnMetadata
                    {
                        Name = column.Name,
                        Type = column.Type,
                        Nullable = column.Nullable,
                        Length = column.Length,
                        Precision = column.Precision,
                        Scale = column.Scale,
                        IsPrimaryKey = column.IsPrimaryKey,
                        IsForeignKey = column.IsForeignKey,
                        Description = column.Description,
                        DatabaseType = column.DatabaseType,
                        PowerBiType = column.PowerBiType
                    }).ToList() ?? new List<ColumnMetadata>()
                }).ToList() ?? new List<TableMetadata>()
            };
        }

        public string ToJson(MetadataDocument metadata, bool indented = true)
        {
            return MetadataSerializer.ToJson(metadata, indented);
        }
    }
}

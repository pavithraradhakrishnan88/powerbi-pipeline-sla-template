using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;
using PowerBiPipelineSlaTemplate.Core.Interfaces;
using PowerBiPipelineSlaTemplate.Core.Models;
using PowerBiPipelineSlaTemplate.Core.Utilities;

namespace PowerBiPipelineSlaTemplate.Core.Csv
{
    public sealed class CsvMetadataExtractor : IMetadataExtractor
    {
        private readonly string _filePath;
        private readonly string? _tableName;

        public CsvMetadataExtractor(string filePath, string? tableName = null)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _tableName = tableName;
        }

        public string SourceName => "CSV";

        public async Task<MetadataDocument> ExtractAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_filePath))
            {
                throw new ArgumentException("A CSV file path is required.", nameof(_filePath));
            }

            if (!File.Exists(_filePath))
            {
                throw new FileNotFoundException("CSV file was not found.", _filePath);
            }

            var rows = new List<List<string>>();
            using var parser = new TextFieldParser(_filePath)
            {
                TextFieldType = FieldType.Delimited,
                Delimiters = new[] { "," },
                HasFieldsEnclosedInQuotes = true
            };

            while (!parser.EndOfData)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = parser.ReadFields();
                if (row == null)
                {
                    continue;
                }

                rows.Add(row.ToList());
                await Task.Yield();
            }

            if (rows.Count == 0)
            {
                return BuildMetadata(new[] { CreateTable(_tableName ?? Path.GetFileNameWithoutExtension(_filePath), Array.Empty<ColumnMetadata>()) });
            }

            var headers = rows[0];
            var dataRows = rows.Skip(1).ToList();
            var columns = new List<ColumnMetadata>();

            for (var index = 0; index < headers.Count; index++)
            {
                var headerName = string.IsNullOrWhiteSpace(headers[index]) ? $"Column{index + 1}" : headers[index].Trim();
                var values = dataRows.Select(row => index < row.Count ? row[index] : string.Empty).ToList();
                var inferredType = DetectColumnType(values);
                var nullable = values.Any(value => string.IsNullOrWhiteSpace(value));
                var length = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Length).DefaultIfEmpty(0).Max();

                columns.Add(new ColumnMetadata
                {
                    Name = headerName,
                    Type = inferredType,
                    Nullable = nullable,
                    Length = length > 0 ? length : null,
                    DatabaseType = DatabaseTypeMapper.Map(inferredType),
                    PowerBiType = DatabaseTypeMapper.MapPowerBiType(inferredType)
                });
            }

            return BuildMetadata(new[] { CreateTable(_tableName ?? Path.GetFileNameWithoutExtension(_filePath), columns) });
        }

        private static string DetectColumnType(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "string";
            }

            var nonEmptyValues = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            if (nonEmptyValues.Count == 0)
            {
                return "string";
            }

            if (nonEmptyValues.All(value => TypeInference.InferType(value) == "boolean"))
            {
                return "boolean";
            }

            if (nonEmptyValues.All(value => TypeInference.InferType(value) == "integer"))
            {
                return "integer";
            }

            if (nonEmptyValues.All(value => TypeInference.InferType(value) == "decimal"))
            {
                return "decimal";
            }

            if (nonEmptyValues.All(value => TypeInference.InferType(value) == "datetime"))
            {
                return "datetime";
            }

            return "string";
        }

        private static MetadataDocument BuildMetadata(IEnumerable<TableMetadata> tables)
        {
            return new MetadataDocument
            {
                Project = new ProjectMetadata
                {
                    Name = "Pipeline SLA Tracker",
                    Version = "1.0",
                    Generator = "PowerBiPipelineSlaTemplate",
                    GeneratedOn = DateTime.UtcNow.ToString("o"),
                    Repository = "powerbi-pipeline-sla-template-git"
                },
                Summary = new MetadataSummary
                {
                    TableCount = tables.Count(table => table != null),
                    ColumnCount = tables.Sum(table => table?.Columns?.Count ?? 0),
                    RelationshipCount = 0,
                    FactTables = tables.Count(table => table?.IsFactTable == true),
                    DimensionTables = tables.Count(table => table?.IsDimensionTable == true),
                    MeasureCount = 0,
                    RowCount = 0
                },
                Tables = tables.Where(table => table != null).Select(table => new TableMetadata
                {
                    Name = table.Name,
                    DisplayFolder = table.DisplayFolder,
                    IsFactTable = table.IsFactTable,
                    IsDimensionTable = table.IsDimensionTable,
                    RowCount = table.RowCount,
                    Columns = table.Columns.Select(column => new ColumnMetadata
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
                    }).ToList()
                }).ToList()
            };
        }

        private static TableMetadata CreateTable(string name, IEnumerable<ColumnMetadata> columns)
        {
            return new TableMetadata
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Table" : name,
                Columns = columns?.ToList() ?? new List<ColumnMetadata>()
            };
        }
    }
}

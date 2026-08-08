using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using PowerBiPipelineSlaTemplate.Core.Interfaces;
using PowerBiPipelineSlaTemplate.Core.Models;
using PowerBiPipelineSlaTemplate.Core.Utilities;

namespace PowerBiPipelineSlaTemplate.Core.Excel
{
    public sealed class ExcelMetadataExtractor : IMetadataExtractor
    {
        private readonly string _filePath;
        private readonly string? _sheetName;
        private readonly string? _tableName;

        public ExcelMetadataExtractor(string filePath, string? sheetName = null, string? tableName = null)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _sheetName = sheetName;
            _tableName = tableName;
        }

        public string SourceName => "Excel";

        public async Task<MetadataDocument> ExtractAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_filePath))
            {
                throw new ArgumentException("An Excel file path is required.", nameof(_filePath));
            }

            if (!File.Exists(_filePath))
            {
                throw new FileNotFoundException("Excel file was not found.", _filePath);
            }

            if (!string.Equals(Path.GetExtension(_filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Only .xlsx files are supported for cross-platform extraction.");
            }

            await using var stream = File.OpenRead(_filePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var workbook = LoadXml(archive, "xl/workbook.xml");
            var workbookRelationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
            var sharedStrings = LoadSharedStrings(archive);
            var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            var relNs = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");

            var sheets = workbook.Descendants(ns + "sheet")
                .Select(sheet => new
                {
                    Name = sheet.Attribute("name")?.Value,
                    RelationshipId = sheet.Attribute(relNs + "id")?.Value
                })
                .Where(sheet => !string.IsNullOrWhiteSpace(sheet.Name) && !string.IsNullOrWhiteSpace(sheet.RelationshipId))
                .ToList();

            if (sheets.Count == 0)
            {
                throw new InvalidOperationException("No worksheets were found in the Excel workbook.");
            }

            var selectedSheet = sheets.FirstOrDefault(sheet => string.Equals(sheet.Name, _sheetName, StringComparison.OrdinalIgnoreCase))
                ?? sheets.First();

            var relationshipTarget = workbookRelationships.Descendants().FirstOrDefault(element => string.Equals(element.Attribute("Id")?.Value, selectedSheet.RelationshipId, StringComparison.OrdinalIgnoreCase))?.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipTarget))
            {
                throw new InvalidOperationException("The workbook relationship for the selected sheet could not be resolved.");
            }

            var worksheetPath = NormalizeWorksheetPath(relationshipTarget);
            var worksheet = LoadXml(archive, worksheetPath);

            var rows = new List<List<string>>();
            foreach (var row in worksheet.Descendants(ns + "row"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = new Dictionary<int, string>();
                foreach (var cell in row.Elements(ns + "c"))
                {
                    var reference = cell.Attribute("r")?.Value;
                    var columnIndex = GetColumnIndex(reference);
                    values[columnIndex] = ResolveCellValue(cell, sharedStrings, ns);
                }

                var maxIndex = values.Keys.Any() ? values.Keys.Max() : -1;
                var rowValues = new List<string>();
                for (var index = 0; index <= maxIndex; index++)
                {
                    rowValues.Add(values.TryGetValue(index, out var value) ? value : string.Empty);
                }

                rows.Add(rowValues);
                await Task.Yield();
            }

            if (rows.Count == 0)
            {
                return BuildMetadata(new[] { CreateTable(_tableName ?? selectedSheet.Name, Array.Empty<ColumnMetadata>()) });
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

            return BuildMetadata(new[] { CreateTable(_tableName ?? selectedSheet.Name, columns) });
        }

        private static XDocument LoadXml(ZipArchive archive, string entryPath)
        {
            var entry = archive.GetEntry(entryPath);
            if (entry == null)
            {
                throw new FileNotFoundException($"The worksheet entry '{entryPath}' was not found in the Excel package.");
            }

            using var stream = entry.Open();
            return XDocument.Load(stream);
        }

        private static List<string> LoadSharedStrings(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return new List<string>();
            }

            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            return document.Descendants(ns + "si")
                .Select(item => string.Concat(item.Descendants(ns + "t").Select(node => node.Value)))
                .ToList();
        }

        private static string ResolveCellValue(XElement cell, IReadOnlyList<string> sharedStrings, XNamespace ns)
        {
            var valueNode = cell.Element(ns + "v");
            if (valueNode == null)
            {
                return string.Empty;
            }

            var cellType = cell.Attribute("t")?.Value;
            if (string.Equals(cellType, "s", StringComparison.Ordinal))
            {
                if (int.TryParse(valueNode.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < sharedStrings.Count)
                {
                    return sharedStrings[index];
                }
            }

            return valueNode.Value;
        }

        private static int GetColumnIndex(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return 0;
            }

            var letters = new string(reference.Where(char.IsLetter).ToArray());
            if (string.IsNullOrEmpty(letters))
            {
                return 0;
            }

            var value = 0;
            foreach (var character in letters)
            {
                value = value * 26 + (character - 'A' + 1);
            }

            return value - 1;
        }

        private static string NormalizeWorksheetPath(string target)
        {
            var normalized = target.Replace('\\', '/').TrimStart('/');
            if (!normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "xl/" + normalized;
            }

            return normalized;
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

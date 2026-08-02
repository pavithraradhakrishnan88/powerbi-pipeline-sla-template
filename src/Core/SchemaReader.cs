using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PowerBiPipelineSlaTemplate.Core.Models;

namespace PowerBiPipelineSlaTemplate.Core
{
    public class SchemaReader
    {
        private const double ForeignKeyCoverageThreshold = 0.95;

        private readonly string _dataDirectory;
        private readonly Action<string>? _logger;
        private readonly bool _throwOnValidationError;

        public SchemaReader(string? dataDirectory = null, Action<string>? logger = null, bool throwOnValidationError = false)
        {
            _dataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "data")
                : dataDirectory;
            _logger = logger;
            _throwOnValidationError = throwOnValidationError;
        }

        public async Task<DatabaseSchema> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(_dataDirectory))
            {
                throw new DirectoryNotFoundException($"Data directory was not found: {_dataDirectory}");
            }

            var files = Directory.GetFiles(_dataDirectory, "*.csv", SearchOption.TopDirectoryOnly)
                .Where(IsEligibleCsvFile)
                .OrderBy(path => path)
                .ToList();

            var results = new List<TableReadResult>();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger?.Invoke($"Processing file: {file}");
                var result = await ReadTableAsync(file, cancellationToken);
                results.Add(result);
            }

            ApplyForeignKeyDetection(results);

            return new DatabaseSchema { Tables = results.Select(result => result.Table).ToList() };
        }

        public DatabaseSchema Read()
        {
            return ReadAsync().GetAwaiter().GetResult();
        }

        private async Task<TableReadResult> ReadTableAsync(string filePath, CancellationToken cancellationToken)
        {
            var fileName = Path.GetFileName(filePath);
            var isFactTable = fileName.StartsWith("Fact_", StringComparison.OrdinalIgnoreCase);
            var isDimensionTable = fileName.StartsWith("Dim_", StringComparison.OrdinalIgnoreCase);

            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(stream);

            var headerLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(headerLine))
            {
                throw new InvalidDataException($"The CSV file '{filePath}' is missing headers.");
            }

            var headers = ParseCsvLine(headerLine);
            if (headers.Count == 0 || headers.All(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException($"The CSV file '{filePath}' is missing headers.");
            }

            var duplicateHeaders = headers
                .Where(header => !string.IsNullOrWhiteSpace(header))
                .GroupBy(header => header.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            if (duplicateHeaders.Count > 0)
            {
                throw new InvalidDataException($"The CSV file '{filePath}' contains duplicate column names: {string.Join(", ", duplicateHeaders)}");
            }

            var columns = headers.Select((header, index) => new ColumnDefinition
            {
                Name = string.IsNullOrWhiteSpace(header) ? $"Column{index + 1}" : header.Trim()
            }).ToList();

            var columnAnalyses = columns.Select(column => new ColumnAnalysis(column.Name)).ToList();
            var rowCount = 0;
            var validationErrors = new List<string>();
            string? sampleValue = null;

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rowCount++;

                var row = ParseCsvLine(line);
                var expectedColumnCount = headers.Count;
                if (row.Count != expectedColumnCount)
                {
                    var issue = $"Row {rowCount} in '{filePath}' has {row.Count} columns but expected {expectedColumnCount}.";
                    if (_throwOnValidationError)
                    {
                        throw new InvalidDataException(issue);
                    }

                    validationErrors.Add(issue);
                    _logger?.Invoke(issue);
                    continue;
                }

                if (sampleValue == null)
                {
                    sampleValue = row.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                }

                for (var index = 0; index < headers.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var value = row[index];
                    var trimmedValue = value?.Trim();
                    var analysis = columnAnalyses[index];
                    if (string.IsNullOrWhiteSpace(trimmedValue))
                    {
                        analysis.NullCount++;
                        continue;
                    }

                    analysis.NonEmptyCount++;
                    analysis.Values.Add(trimmedValue);
                    analysis.MaxLength = Math.Max(analysis.MaxLength, trimmedValue.Length);
                }
            }

            var inferredColumns = new List<ColumnDefinition>();
            for (var index = 0; index < columns.Count; index++)
            {
                var column = columns[index];
                var analysis = columnAnalyses[index];
                var nonEmptyValues = analysis.Values.ToList();
                var inferredType = InferType(nonEmptyValues);
                var distinctCount = analysis.Values.Count;
                var nullable = analysis.NullCount > 0;
                var minimum = GetMinimum(nonEmptyValues, inferredType);
                var maximum = GetMaximum(nonEmptyValues, inferredType);
                var sample = nonEmptyValues.FirstOrDefault();
                var isPrimaryKeyCandidate = IsPrimaryKeyCandidate(column.Name, rowCount, analysis.NonEmptyCount, analysis.NullCount, distinctCount);

                column.Type = inferredType;
                column.Nullable = nullable;
                column.Length = analysis.MaxLength;
                column.DistinctCount = distinctCount;
                column.SampleValue = sample;
                column.Minimum = minimum;
                column.Maximum = maximum;
                column.IsPrimaryKeyCandidate = isPrimaryKeyCandidate;
                analysis.IsPrimaryKeyCandidate = isPrimaryKeyCandidate;
                inferredColumns.Add(column);
            }

            var table = new TableDefinition
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Columns = inferredColumns,
                RowCount = rowCount,
                SampleValue = sampleValue,
                DistinctCount = inferredColumns.SelectMany(column => column.SampleValue != null ? new[] { column.SampleValue } : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                IsFactTable = isFactTable,
                IsDimensionTable = isDimensionTable
            };

            _logger?.Invoke($"Completed {filePath}: rows={rowCount}, columns={headers.Count}, validations={validationErrors.Count}");

            return new TableReadResult(table, columnAnalyses);
        }

        private void ApplyForeignKeyDetection(IReadOnlyList<TableReadResult> results)
        {
            var primaryKeyCandidates = results
                .SelectMany(result => result.ColumnAnalyses.Select((analysis, index) => new { result, analysis, index }))
                .Where(item => item.analysis.IsPrimaryKeyCandidate)
                .Select(item => new
                {
                    TableName = item.result.Table.Name,
                    ColumnName = item.result.Table.Columns[item.index].Name,
                    Values = item.analysis.Values
                })
                .ToList();

            foreach (var result in results)
            {
                foreach (var column in result.Table.Columns)
                {
                    if (column.IsPrimaryKeyCandidate)
                    {
                        continue;
                    }

                    var columnIndex = result.Table.Columns.IndexOf(column);
                    var columnAnalysis = result.ColumnAnalyses[columnIndex];
                    if (!column.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var matchingPrimaryKey = primaryKeyCandidates
                        .Where(candidate => candidate.TableName != result.Table.Name)
                        .Where(candidate => candidate.Values.Count > 0)
                        .Where(candidate => IsHighCoverageMatch(columnAnalysis.Values, candidate.Values))
                        .OrderByDescending(candidate => candidate.Values.Count)
                        .FirstOrDefault();

                    column.IsForeignKeyCandidate = matchingPrimaryKey != null;
                }
            }
        }

        private static bool IsHighCoverageMatch(IReadOnlyCollection<string> foreignKeyValues, IReadOnlyCollection<string> primaryKeyValues)
        {
            if (foreignKeyValues == null || foreignKeyValues.Count == 0 || primaryKeyValues == null || primaryKeyValues.Count == 0)
            {
                return false;
            }

            var coveredCount = foreignKeyValues.Count(value => primaryKeyValues.Contains(value, StringComparer.OrdinalIgnoreCase));
            var coverageRatio = (double)coveredCount / foreignKeyValues.Count;

            return coverageRatio >= ForeignKeyCoverageThreshold;
        }

        private static bool IsEligibleCsvFile(string path)
        {
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (fileName.StartsWith(".", StringComparison.Ordinal))
            {
                return false;
            }

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Hidden) != 0 || (attributes & FileAttributes.System) != 0)
            {
                return false;
            }

            return fileName.StartsWith("Fact_", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("Dim_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPrimaryKeyCandidate(string columnName, int rowCount, int nonEmptyCount, int nullCount, int distinctCount)
        {
            if (rowCount <= 0)
            {
                return false;
            }

            if (!columnName.Equals("Id", StringComparison.OrdinalIgnoreCase) && !columnName.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return nullCount == 0 && nonEmptyCount == rowCount && distinctCount == rowCount;
        }

        private static string InferType(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "string";
            }

            if (values.All(IsBoolean))
            {
                return "bool";
            }

            if (values.All(IsInteger))
            {
                return "int";
            }

            if (values.All(IsDecimal))
            {
                return "decimal";
            }

            if (values.All(IsDateTime))
            {
                return "DateTime";
            }

            return "string";
        }

        private static bool IsBoolean(string value) => bool.TryParse(value, out _);

        private static bool IsInteger(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

        private static bool IsDecimal(string value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

        private static bool IsDateTime(string value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out _);

        private static string? GetMinimum(IReadOnlyList<string> values, string inferredType)
        {
            if (values == null || values.Count == 0)
            {
                return null;
            }

            return inferredType switch
            {
                "int" => values.Select(int.Parse).OrderBy(value => value).First().ToString(CultureInfo.InvariantCulture),
                "decimal" => values.Select(decimal.Parse).OrderBy(value => value).First().ToString(CultureInfo.InvariantCulture),
                "DateTime" => values.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).OrderBy(value => value).First().ToString("o"),
                _ => values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).First()
            };
        }

        private static string? GetMaximum(IReadOnlyList<string> values, string inferredType)
        {
            if (values == null || values.Count == 0)
            {
                return null;
            }

            return inferredType switch
            {
                "int" => values.Select(int.Parse).OrderByDescending(value => value).First().ToString(CultureInfo.InvariantCulture),
                "decimal" => values.Select(decimal.Parse).OrderByDescending(value => value).First().ToString(CultureInfo.InvariantCulture),
                "DateTime" => values.Select(value => DateTime.Parse(value, CultureInfo.InvariantCulture)).OrderByDescending(value => value).First().ToString("o"),
                _ => values.OrderByDescending(value => value, StringComparer.OrdinalIgnoreCase).First()
            };
        }

        private static List<string> ParseCsvLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return new List<string>();
            }

            var values = new List<string>();
            var current = string.Empty;
            var inQuotes = false;

            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                if (character == '"')
                {
                    if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current += '"';
                        index++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (character == ',' && !inQuotes)
                {
                    values.Add(current);
                    current = string.Empty;
                }
                else
                {
                    current += character;
                }
            }

            values.Add(current);
            return values;
        }

        private sealed class TableReadResult
        {
            public TableReadResult(TableDefinition table, List<ColumnAnalysis> columnAnalyses)
            {
                Table = table;
                ColumnAnalyses = columnAnalyses;
            }

            public TableDefinition Table { get; }
            public List<ColumnAnalysis> ColumnAnalyses { get; }
        }

        private sealed class ColumnAnalysis
        {
            public ColumnAnalysis(string name)
            {
                Name = name;
            }

            public string Name { get; }
            public HashSet<string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
            public int NonEmptyCount { get; set; }
            public int NullCount { get; set; }
            public int MaxLength { get; set; }
            public bool IsPrimaryKeyCandidate { get; set; }
        }
    }
}

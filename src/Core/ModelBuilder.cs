using System;
using System.Collections.Generic;
using System.Linq;
using PowerBiPipelineSlaTemplate.Core.Models;
using PowerBiPipelineSlaTemplate.Core.Serialization;

namespace PowerBiPipelineSlaTemplate.Core
{
    public class ModelBuilder
    {
        public ModelBuildResult Build(DatabaseSchema schema)
        {
            ArgumentNullException.ThrowIfNull(schema);

            var tables = (schema.Tables ?? new List<TableDefinition>())
                .Where(table => table != null)
                .Select(table => BuildTable(table!))
                .ToList();

            var relationships = BuildRelationships(tables);
            var formatting = BuildFormatting(tables);
            var displayFolders = BuildDisplayFolderMap(tables);
            var validationErrors = ValidateModel(tables, relationships);

            return new ModelBuildResult
            {
                Tables = tables,
                Relationships = relationships,
                Formatting = formatting,
                DisplayFolders = displayFolders,
                ValidationErrors = validationErrors
            };
        }

        public string ToJson(ModelBuildResult model, bool indented = true)
        {
            ArgumentNullException.ThrowIfNull(model);

            return model.ToJson(indented);
        }

        private static BuiltTable BuildTable(TableDefinition table)
        {
            var columns = (table.Columns ?? new List<ColumnDefinition>())
                .Where(column => column != null)
                .Select(column => BuildColumn(table, column!))
                .ToList();

            return new BuiltTable
            {
                Name = string.IsNullOrWhiteSpace(table.Name) ? "Table" : table.Name,
                DisplayFolder = InferTableDisplayFolder(table),
                IsFactTable = table.IsFactTable,
                IsDimensionTable = table.IsDimensionTable,
                RowCount = table.RowCount,
                Columns = columns
            };
        }

        private static BuiltColumn BuildColumn(TableDefinition table, ColumnDefinition column)
        {
            var displayFolder = InferColumnDisplayFolder(table, column);
            var formatString = InferFormatString(column, table);
            var description = BuildDescription(table, column);

            return new BuiltColumn
            {
                Name = string.IsNullOrWhiteSpace(column.Name) ? "Column" : column.Name,
                DataType = MapType(column.Type),
                Nullable = column.Nullable,
                IsPrimaryKey = column.IsPrimaryKeyCandidate,
                IsForeignKey = column.IsForeignKeyCandidate,
                DisplayFolder = displayFolder,
                FormatString = formatString,
                Description = description
            };
        }

        private static List<ModelRelationship> BuildRelationships(IReadOnlyList<BuiltTable> tables)
        {
            var relationships = new List<ModelRelationship>();
            var seenRelationships = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var table in tables)
            {
                foreach (var column in table.Columns.Where(column => column.IsForeignKey))
                {
                    if (!TryResolveRelationship(table, column, tables, out var relationship))
                    {
                        continue;
                    }

                    var relationshipKey = $"{relationship.FromTable}.{relationship.FromColumn}->{relationship.ToTable}.{relationship.ToColumn}";
                    if (seenRelationships.Add(relationshipKey))
                    {
                        relationships.Add(relationship);
                    }
                }
            }

            return relationships;
        }

        private static Dictionary<string, string> BuildFormatting(IReadOnlyList<BuiltTable> tables)
        {
            var formatting = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var table in tables)
            {
                foreach (var column in table.Columns)
                {
                    if (!string.IsNullOrWhiteSpace(column.FormatString))
                    {
                        formatting[$"{table.Name}.{column.Name}"] = column.FormatString;
                    }
                }
            }

            return formatting;
        }

        private static Dictionary<string, string> BuildDisplayFolderMap(IReadOnlyList<BuiltTable> tables)
        {
            var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var table in tables)
            {
                if (!string.IsNullOrWhiteSpace(table.DisplayFolder))
                {
                    folders[table.Name] = table.DisplayFolder;
                }

                foreach (var column in table.Columns)
                {
                    if (!string.IsNullOrWhiteSpace(column.DisplayFolder))
                    {
                        folders[$"{table.Name}.{column.Name}"] = column.DisplayFolder;
                    }
                }
            }

            return folders;
        }

        private static string MapType(string? sourceType)
        {
            var normalizedType = (sourceType ?? "string").Trim().ToLowerInvariant();

            return normalizedType switch
            {
                "bool" or "boolean" => "Boolean",
                "int" or "integer" or "long" or "int64" => "Int64",
                "double" or "float" or "single" or "real" => "Double",
                "decimal" or "numeric" or "number" => "Decimal",
                "datetime" or "timestamp" => "DateTime",
                "date" => "Date",
                "guid" or "uniqueidentifier" => "Text",
                "string" or "varchar" or "nvarchar" or "char" or "text" => "Text",
                _ => "Text"
            };
        }

        private static string InferTableDisplayFolder(TableDefinition table)
        {
            var tableName = table.Name ?? string.Empty;
            if (table.IsFactTable || tableName.Contains("Fact", StringComparison.OrdinalIgnoreCase))
            {
                return "Facts";
            }

            if (table.IsDimensionTable || tableName.StartsWith("Dim_", StringComparison.OrdinalIgnoreCase) || tableName.Contains("Date", StringComparison.OrdinalIgnoreCase) || tableName.Contains("Calendar", StringComparison.OrdinalIgnoreCase))
            {
                return tableName.Contains("Date", StringComparison.OrdinalIgnoreCase) || tableName.Contains("Calendar", StringComparison.OrdinalIgnoreCase) ? "Dates" : "Dimensions";
            }

            return "Other";
        }

        private static string InferColumnDisplayFolder(TableDefinition table, ColumnDefinition column)
        {
            var normalizedName = (column.Name ?? string.Empty).ToLowerInvariant();
            if (normalizedName.Contains("date") || normalizedName.Contains("month") || normalizedName.Contains("day") || normalizedName.Contains("year") || normalizedName.Contains("timestamp"))
            {
                return "Dates";
            }

            if (normalizedName.Contains("id") && normalizedName != "id")
            {
                return "Keys";
            }

            if (normalizedName.Contains("name") || normalizedName.Contains("description") || normalizedName.Contains("category") || normalizedName.Contains("status"))
            {
                return "Attributes";
            }

            if (table.IsFactTable || normalizedName.Contains("amount") || normalizedName.Contains("duration") || normalizedName.Contains("count") || normalizedName.Contains("rate") || normalizedName.Contains("percent") || normalizedName.Contains("total") || normalizedName.Contains("measure"))
            {
                return "Measures";
            }

            return string.Empty;
        }

        private static string InferFormatString(ColumnDefinition column, TableDefinition table)
        {
            var normalizedName = (column.Name ?? string.Empty).ToLowerInvariant();
            if (column.Type.Equals("DateTime", StringComparison.OrdinalIgnoreCase) || column.Type.Equals("Date", StringComparison.OrdinalIgnoreCase))
            {
                return "yyyy-MM-dd";
            }

            if (column.Type.Equals("decimal", StringComparison.OrdinalIgnoreCase) || column.Type.Equals("int", StringComparison.OrdinalIgnoreCase) || column.Type.Equals("int64", StringComparison.OrdinalIgnoreCase) || column.Type.Equals("double", StringComparison.OrdinalIgnoreCase))
            {
                if (normalizedName.Contains("percent") || normalizedName.Contains("rate") || normalizedName.Contains("pct") || normalizedName.Contains("ratio"))
                {
                    return "0.00%";
                }

                if (normalizedName.Contains("amount") || normalizedName.Contains("cost") || normalizedName.Contains("price") || normalizedName.Contains("total") || normalizedName.Contains("revenue") || normalizedName.Contains("sales"))
                {
                    return "$#,##0.00";
                }

                if (normalizedName.Contains("count") || normalizedName.Contains("number") || normalizedName.Contains("qty"))
                {
                    return "#,##0";
                }
            }

            return string.Empty;
        }

        private static string BuildDescription(TableDefinition table, ColumnDefinition column)
        {
            var tableName = HumanizeName(table.Name);
            var columnName = HumanizeName(column.Name);

            if (column.IsPrimaryKeyCandidate)
            {
                return $"Primary key for {tableName}.";
            }

            if (column.IsForeignKeyCandidate)
            {
                return $"Foreign key used by {tableName}.";
            }

            if (column.Name.Contains("Date", StringComparison.OrdinalIgnoreCase) || column.Name.Contains("Time", StringComparison.OrdinalIgnoreCase) || column.Name.Contains("Month", StringComparison.OrdinalIgnoreCase))
            {
                return $"Date attribute for {tableName}.";
            }

            if (column.Name.Contains("Amount", StringComparison.OrdinalIgnoreCase) || column.Name.Contains("Duration", StringComparison.OrdinalIgnoreCase) || column.Name.Contains("Count", StringComparison.OrdinalIgnoreCase) || column.Name.Contains("Rate", StringComparison.OrdinalIgnoreCase))
            {
                return $"Measure attribute for {tableName}.";
            }

            return $"{columnName} in {tableName}.";
        }

        private static bool TryResolveRelationship(BuiltTable sourceTable, BuiltColumn foreignKeyColumn, IReadOnlyList<BuiltTable> tables, out ModelRelationship relationship)
        {
            relationship = null!;
            if (string.IsNullOrWhiteSpace(foreignKeyColumn.Name))
            {
                return false;
            }

            var baseToken = ExtractBaseToken(foreignKeyColumn.Name);
            var bestScore = -1;
            BuiltTable? bestTable = null;
            BuiltColumn? bestColumn = null;

            foreach (var candidateTable in tables.Where(candidate => !string.Equals(candidate.Name, sourceTable.Name, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var candidateColumn in candidateTable.Columns)
                {
                    var score = ScoreRelationshipCandidate(sourceTable, foreignKeyColumn, candidateTable, candidateColumn, baseToken);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTable = candidateTable;
                        bestColumn = candidateColumn;
                    }
                }
            }

            if (bestTable == null || bestColumn == null || bestScore < 70)
            {
                return false;
            }

            relationship = new ModelRelationship
            {
                FromTable = sourceTable.Name,
                FromColumn = foreignKeyColumn.Name,
                ToTable = bestTable.Name,
                ToColumn = bestColumn.Name,
                Cardinality = "manyToOne",
                CrossFilteringBehavior = "oneDirection"
            };

            return true;
        }

        private static int ScoreRelationshipCandidate(BuiltTable sourceTable, BuiltColumn foreignKeyColumn, BuiltTable targetTable, BuiltColumn targetColumn, string baseToken)
        {
            var foreignKeyName = NormalizeToken(foreignKeyColumn.Name);
            var targetColumnName = NormalizeToken(targetColumn.Name);
            var targetTableName = NormalizeToken(targetTable.Name);
            var sourceTableName = NormalizeToken(sourceTable.Name);
            var score = 0;

            if (targetColumn.IsPrimaryKey)
            {
                score += 60;
            }

            if (targetColumnName.Equals("id") || targetColumnName.Equals("key") || targetColumnName.Equals(baseToken))
            {
                score += 25;
            }

            if (targetColumnName.Equals(foreignKeyName) || targetColumnName.Equals(RemoveSuffix(foreignKeyColumn.Name, "Id")) || targetColumnName.Equals(RemoveSuffix(foreignKeyColumn.Name, "Key")))
            {
                score += 20;
            }

            if (targetTableName.Contains(baseToken) || targetTableName.Contains(foreignKeyName))
            {
                score += 20;
            }

            if (targetColumnName.Contains(baseToken) || foreignKeyName.Contains(baseToken))
            {
                score += 15;
            }

            return score;
        }

        private static List<string> ValidateModel(IReadOnlyList<BuiltTable> tables, IReadOnlyList<ModelRelationship> relationships)
        {
            var errors = new List<string>();
            var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var table in tables)
            {
                if (string.IsNullOrWhiteSpace(table.Name))
                {
                    errors.Add("Encountered a table without a name.");
                    continue;
                }

                if (!tableNames.Add(table.Name))
                {
                    errors.Add($"Duplicate table name detected: {table.Name}");
                }

                var seenColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var column in table.Columns)
                {
                    if (string.IsNullOrWhiteSpace(column.Name))
                    {
                        errors.Add($"Table {table.Name} contains a column without a name.");
                        continue;
                    }

                    if (!seenColumns.Add(column.Name))
                    {
                        errors.Add($"Table {table.Name} contains a duplicate column name: {column.Name}");
                    }
                }
            }

            var tableLookup = tables.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var relationship in relationships)
            {
                if (!tableLookup.ContainsKey(relationship.FromTable))
                {
                    errors.Add($"Relationship references missing source table: {relationship.FromTable}");
                    continue;
                }

                if (!tableLookup.ContainsKey(relationship.ToTable))
                {
                    errors.Add($"Relationship references missing target table: {relationship.ToTable}");
                    continue;
                }

                var fromTable = tableLookup[relationship.FromTable];
                var toTable = tableLookup[relationship.ToTable];

                if (!fromTable.Columns.Any(column => string.Equals(column.Name, relationship.FromColumn, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Relationship references missing source column: {relationship.FromTable}.{relationship.FromColumn}");
                }

                if (!toTable.Columns.Any(column => string.Equals(column.Name, relationship.ToColumn, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Relationship references missing target column: {relationship.ToTable}.{relationship.ToColumn}");
                }
            }

            return errors;
        }

        private static string RemoveSuffix(string value, string suffix)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(suffix))
            {
                return value;
            }

            return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? value[..^suffix.Length]
                : value;
        }

        private static string ExtractBaseToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = NormalizeToken(value);
            return RemoveSuffix(RemoveSuffix(RemoveSuffix(RemoveSuffix(normalized, "id"), "key"), "code"), "number");
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value.Where(character => char.IsLetterOrDigit(character)).ToArray()).ToLowerInvariant();
        }

        private static string HumanizeName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "column";
            }

            return string.Join(" ", value.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
        }
    }

    public class ModelBuildResult
    {
        public List<BuiltTable> Tables { get; set; } = new();
        public List<ModelRelationship> Relationships { get; set; } = new();
        public Dictionary<string, string> Formatting { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> DisplayFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ValidationErrors { get; set; } = new();
    }

    public class BuiltTable
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayFolder { get; set; } = string.Empty;
        public bool IsFactTable { get; set; }
        public bool IsDimensionTable { get; set; }
        public int RowCount { get; set; }
        public List<BuiltColumn> Columns { get; set; } = new();
    }

    public class BuiltColumn
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = "Text";
        public bool Nullable { get; set; } = true;
        public bool IsPrimaryKey { get; set; }
        public bool IsForeignKey { get; set; }
        public string DisplayFolder { get; set; } = string.Empty;
        public string FormatString { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class ModelRelationship
    {
        public string FromTable { get; set; } = string.Empty;
        public string FromColumn { get; set; } = string.Empty;
        public string ToTable { get; set; } = string.Empty;
        public string ToColumn { get; set; } = string.Empty;
        public string Cardinality { get; set; } = "manyToOne";
        public string CrossFilteringBehavior { get; set; } = "oneDirection";
    }
}

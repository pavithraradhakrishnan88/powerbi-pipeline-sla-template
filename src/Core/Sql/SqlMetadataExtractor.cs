using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PowerBiPipelineSlaTemplate.Core.Interfaces;
using PowerBiPipelineSlaTemplate.Core.Models;
using PowerBiPipelineSlaTemplate.Core.Utilities;

namespace PowerBiPipelineSlaTemplate.Core.Sql
{
    public sealed class SqlMetadataExtractor : IMetadataExtractor
    {
        private readonly string _connectionString;
        private readonly string _query;
        private readonly string? _tableName;

        public SqlMetadataExtractor(string connectionString, string query, string? tableName = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _query = query ?? throw new ArgumentNullException(nameof(query));
            _tableName = tableName;
        }

        public string SourceName => "SQL";

        public async Task<MetadataDocument> ExtractAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                throw new ArgumentException("A SQL connection string is required.", nameof(_connectionString));
            }

            if (string.IsNullOrWhiteSpace(_query))
            {
                throw new ArgumentException("A SQL query is required.", nameof(_query));
            }

            var providerName = ResolveProviderName(_connectionString);
            var factory = DbProviderFactories.GetFactory(providerName);
            await using var connection = (DbConnection?)factory.CreateConnection();
            if (connection == null)
            {
                throw new InvalidOperationException("The database provider could not create a connection instance.");
            }

            connection.ConnectionString = _connectionString;
            await connection.OpenAsync(cancellationToken);

            await using var command = factory.CreateCommand();
            command.Connection = connection;
            command.CommandText = _query;

            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken);
            var schemaTable = reader.GetSchemaTable();
            if (schemaTable == null)
            {
                return BuildMetadata(new[] { CreateTable(_tableName ?? "QueryResult", Array.Empty<ColumnMetadata>()) });
            }

            var columns = new List<ColumnMetadata>();
            foreach (DataRow row in schemaTable.Rows)
            {
                var columnName = row["ColumnName"]?.ToString() ?? "Column";
                var clrType = row["DataType"] as Type;
                var inferredType = TypeInference.InferTypeFromClrType(clrType);
                var isNullable = row["AllowDBNull"] != null && Convert.ToBoolean(row["AllowDBNull"]);
                var dataTypeName = row["DataTypeName"]?.ToString() ?? inferredType;
                var columnSize = TryGetInt(row, "ColumnSize");
                var numericPrecision = TryGetInt(row, "NumericPrecision");
                var numericScale = TryGetInt(row, "NumericScale");
                var isPrimaryKey = row["IsKey"] != null && Convert.ToBoolean(row["IsKey"]);

                columns.Add(new ColumnMetadata
                {
                    Name = columnName,
                    Type = inferredType,
                    Nullable = isNullable,
                    Length = columnSize,
                    Precision = numericPrecision,
                    Scale = numericScale,
                    IsPrimaryKey = isPrimaryKey,
                    DatabaseType = dataTypeName,
                    Description = string.Empty,
                    PowerBiType = DatabaseTypeMapper.MapPowerBiType(inferredType)
                });
            }

            return BuildMetadata(new[] { CreateTable(_tableName ?? "QueryResult", columns) });
        }

        private static string ResolveProviderName(string connectionString)
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            if (builder.ContainsKey("Provider"))
            {
                return builder["Provider"].ToString();
            }

            return "System.Data.SqlClient";
        }

        private static int? TryGetInt(DataRow row, string key)
        {
            if (row[key] == null || row[key] == DBNull.Value)
            {
                return null;
            }

            return Convert.ToInt32(row[key], CultureInfo.InvariantCulture);
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

using System.Collections.Generic;
using PowerBiPipelineSlaTemplate.Core.Models;

namespace PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;

public static class SampleMetadata
{
    public static DatabaseSchema Normal()
    {
        return new DatabaseSchema
        {
            Tables = new List<TableDefinition>
            {
                new()
                {
                    Name = "Dim_Category",
                    IsDimensionTable = true,
                    RowCount = 3,
                    Columns = new List<ColumnDefinition>
                    {
                        new() { Name = "CategoryId", Type = "int", IsPrimaryKeyCandidate = true, Nullable = false },
                        new() { Name = "CategoryName", Type = "string", Nullable = false }
                    }
                },
                new()
                {
                    Name = "Fact_Pipeline_SampleData",
                    IsFactTable = true,
                    RowCount = 3,
                    Columns = new List<ColumnDefinition>
                    {
                        new() { Name = "PipelineId", Type = "string", Nullable = false },
                        new() { Name = "CategoryId", Type = "int", IsForeignKeyCandidate = true, Nullable = false },
                        new() { Name = "DurationHours", Type = "decimal", Nullable = false },
                        new() { Name = "SLA_Target_Hrs", Type = "int", Nullable = false }
                    }
                }
            }
        };
    }

    public static DatabaseSchema Empty()
    {
        return new DatabaseSchema
        {
            Tables = new List<TableDefinition>()
        };
    }

    public static DatabaseSchema WithDuplicateColumns()
    {
        return new DatabaseSchema
        {
            Tables = new List<TableDefinition>
            {
                new()
                {
                    Name = "Fact_Duplicate",
                    IsFactTable = true,
                    Columns = new List<ColumnDefinition>
                    {
                        new() { Name = "Id", Type = "int", IsPrimaryKeyCandidate = true, Nullable = false },
                        new() { Name = "Id", Type = "int", Nullable = false }
                    }
                }
            }
        };
    }
}

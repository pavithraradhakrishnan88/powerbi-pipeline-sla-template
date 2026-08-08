using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerBiPipelineSlaTemplate.Core.Models;

namespace PowerBiPipelineSlaTemplate.Core;

/// <summary>
/// Reads and validates the repository metadata document.
/// </summary>
public sealed class MetadataReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MetadataDocument Read(string metadataPath)
    {
        if (string.IsNullOrWhiteSpace(metadataPath))
            throw new ArgumentException("Metadata path is required.", nameof(metadataPath));

        if (!File.Exists(metadataPath))
            throw new FileNotFoundException("Metadata file was not found.", metadataPath);

        var json = File.ReadAllText(metadataPath);

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Metadata file is empty.");

        using var document = JsonDocument.Parse(json);
        ValidateRequiredSections(document.RootElement);

        var metadata = System.Text.Json.JsonSerializer.Deserialize<MetadataDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Metadata could not be deserialized.");

        if (metadata.Project is null)
            metadata.Project = new ProjectMetadata();

        if (metadata.Summary is null)
            metadata.Summary = new MetadataSummary();

        if (metadata.Tables is null)
            metadata.Tables = new();

        ValidateTables(metadata);
        NormalizeColumns(metadata);

        return metadata;
    }

    private static void ValidateRequiredSections(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Metadata root must be a JSON object.");

        RequireObject(root, "Project");
        RequireObject(root, "Summary");
        RequireArray(root, "Tables");
    }

    private static void ValidateTables(MetadataDocument metadata)
    {
        if (metadata.Tables is null || metadata.Tables.Count == 0)
            throw new InvalidDataException("Metadata must contain at least one table.");

        var missingNames = metadata.Tables
            .Where(t => t is null || string.IsNullOrWhiteSpace(t.Name))
            .Select((_, index) => index)
            .ToArray();

        if (missingNames.Length > 0)
            throw new InvalidDataException(
                $"Metadata contains tables without names at indexes: {string.Join(", ", missingNames)}.");
    }

    private static void NormalizeColumns(MetadataDocument metadata)
    {
        foreach (var table in metadata.Tables)
        {
            table.Columns ??= new();

            foreach (var column in table.Columns)
            {
                if (column is null)
                    continue;

                if (!string.IsNullOrWhiteSpace(column.DataType))
                    column.Type = column.DataType;
                else if (string.IsNullOrWhiteSpace(column.Type))
                    column.Type = column.PowerBiType;
            }
        }
    }

    private static void RequireObject(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Metadata is missing required object section '{propertyName}'.");
    }

    private static void RequireArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Metadata is missing required array section '{propertyName}'.");
    }
}

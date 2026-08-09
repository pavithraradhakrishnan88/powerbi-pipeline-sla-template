using System;
using System.IO;
using System.Text.Json.Nodes;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class PbirFieldBindingTests
{
    [Fact]
    public void TemplateVisuals_ShouldUseCorrectQueryRefForMeasureAndColumnFields()
    {
        var repoRoot = WorkspacePaths.FindRepoRoot();
        var reportRoot = Path.Combine(repoRoot, "pbip", "Pipeline_SLA_Tracker.Report");
        var pagesRoot = Path.Combine(reportRoot, "definition", "pages");

        var visualFiles = Directory.GetFiles(pagesRoot, "visual.json", SearchOption.AllDirectories);
        visualFiles.Should().NotBeEmpty();

        foreach (var visualFile in visualFiles)
        {
            var root = JsonNode.Parse(File.ReadAllText(visualFile));
            root.Should().NotBeNull();
            ValidateFieldBindings(root!, visualFile);
        }
    }

    private static void ValidateFieldBindings(JsonNode node, string visualFile)
    {
        if (node is JsonObject obj)
        {
            if (obj["field"] is JsonObject field && obj["queryRef"] is JsonValue queryRefNode)
            {
                var queryRef = queryRefNode.GetValue<string>();

                if (field["Measure"] is JsonObject measure &&
                    measure["Expression"]?["SourceRef"]?["Entity"] is JsonValue measureEntityNode &&
                    measure["Property"] is JsonValue measurePropertyNode)
                {
                    var entity = measureEntityNode.GetValue<string>();
                    var property = measurePropertyNode.GetValue<string>();

                    queryRef.Should().Be(
                        $"_Measures.{property}",
                        $"measure queryRef in '{visualFile}' must use the semantic-model measure table (source entity '{entity}')");
                }

                if (field["Column"] is JsonObject column &&
                    column["Expression"]?["SourceRef"]?["Entity"] is JsonValue columnEntityNode &&
                    column["Property"] is JsonValue columnPropertyNode)
                {
                    var entity = columnEntityNode.GetValue<string>();
                    var property = columnPropertyNode.GetValue<string>();

                    queryRef.Should().Be(
                        $"{entity}.{property}",
                        $"column queryRef in '{visualFile}' must remain bound to its source column");
                }
            }

            foreach (var property in obj)
            {
                if (property.Value is not null)
                {
                    ValidateFieldBindings(property.Value, visualFile);
                }
            }

            return;
        }

        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    ValidateFieldBindings(item, visualFile);
                }
            }
        }
    }
}

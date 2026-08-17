using System;
using System.IO;
using System.Reflection;
using System.Text;
using Xunit;
using PowerBiPipelineSlaTemplate.Core;

namespace PowerBiPipelineSlaTemplate.Tests;

public sealed class PatchMeasuresExpressionReconciliationTests
{
    [Fact]
    public void PatchMeasures_ReplacesExpressionForExistingMeasure_WhenCanonicalExpressionDiffers()
    {
        var root = Path.Combine(Path.GetTempPath(), "PatchMeasuresRegression", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var factPath = Path.Combine(root, "Fact_Pipeline_SampleData.tmdl");
            var definitionsPath = Path.Combine(root, "MeasureDefinitions.json");

            File.WriteAllText(factPath, "\tmeasure 'Floating Bar Status' = IF(MAX(Fact_Pipeline_SampleData[SLAStatus])=\"Missed\",\"Breach\",\"WithinSLA\")\r\n\r\n\tpartition Fact_Pipeline_SampleData = m\r\n", new UTF8Encoding(false));
            File.WriteAllText(definitionsPath, "{\"measures\":[{\"Table\":\"Fact_Pipeline_SampleData\",\"Name\":\"Floating Bar Status\",\"Expression\":\"IF(MAX(Fact_Pipeline_SampleData[SLAStatus]) = \\\"Missed\\\", \\\"Breach\\\", \\\"Within SLA\\\")\"}]}", new UTF8Encoding(false));

            InvokePatchMeasures(factPath, definitionsPath);

            var result = File.ReadAllText(factPath);
            Assert.Contains("\"Within SLA\"", result, StringComparison.Ordinal);
            Assert.DoesNotContain("\"WithinSLA\"", result, StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(result, "measure 'Floating Bar Status'"));
            Assert.Contains("partition Fact_Pipeline_SampleData = m", result, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PatchMeasures_AppendsMissingMeasure_WithoutDuplicatingExistingMeasure()
    {
        var root = Path.Combine(Path.GetTempPath(), "PatchMeasuresRegression", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var factPath = Path.Combine(root, "Fact_Pipeline_SampleData.tmdl");
            var definitionsPath = Path.Combine(root, "MeasureDefinitions.json");

            File.WriteAllText(factPath, "\tmeasure 'Existing Measure' = 1\r\n\r\n\tpartition Fact_Pipeline_SampleData = m\r\n", new UTF8Encoding(false));
            File.WriteAllText(definitionsPath, "{\"measures\":[{\"Table\":\"Fact_Pipeline_SampleData\",\"Name\":\"Existing Measure\",\"Expression\":\"1\"},{\"Table\":\"Fact_Pipeline_SampleData\",\"Name\":\"New Regression Measure\",\"Expression\":\"2\"}]}", new UTF8Encoding(false));

            InvokePatchMeasures(factPath, definitionsPath);

            var result = File.ReadAllText(factPath);
            Assert.Equal(1, CountOccurrences(result, "measure 'Existing Measure'"));
            Assert.Equal(1, CountOccurrences(result, "measure 'New Regression Measure'"));
            Assert.Contains("measure 'New Regression Measure' = 2", result, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void InvokePatchMeasures(string factPath, string definitionsPath)
    {
        // Resolve the production type from the referenced Core assembly, not the test assembly.
        var coreAssembly = typeof(PipelineOrchestrator).Assembly;
        var type = coreAssembly.GetType(
            "PowerBiPipelineSlaTemplate.Core.Pbip.TemplateSemanticModelMetadataPatcher",
            throwOnError: true)!;
        var method = type.GetMethod("PatchMeasures", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method!.Invoke(null, new object?[] { factPath, definitionsPath, null });
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}

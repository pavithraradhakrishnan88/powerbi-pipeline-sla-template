using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;

namespace PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;

public static class SampleCsvGenerator
{
    public static string CopyScenarioToWorkspace(string scenarioName, string destinationDataDirectory)
    {
        var repoRoot = WorkspacePaths.FindRepoRoot();
        var sourceDirectory = Path.Combine(repoRoot, "tests", "TestData", scenarioName);
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Scenario directory not found: {sourceDirectory}");
        }

        Directory.CreateDirectory(destinationDataDirectory);

        foreach (var sourceFile in Directory.GetFiles(sourceDirectory, "*.csv"))
        {
            var destinationFile = Path.Combine(destinationDataDirectory, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }

        return destinationDataDirectory;
    }

    public static string GenerateLargeDataset(string destinationDataDirectory, int rowCount = 1000)
    {
        if (rowCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount));
        }

        Directory.CreateDirectory(destinationDataDirectory);
        var dimPath = Path.Combine(destinationDataDirectory, "Dim_Category.csv");
        var factPath = Path.Combine(destinationDataDirectory, "Fact_Pipeline_SampleData.csv");

        File.WriteAllLines(dimPath, new[]
        {
            "CategoryId,CategoryName",
            "1,Ingestion",
            "2,Transformation",
            "3,Delivery"
        });

        var lines = new List<string> { "PipelineID,CategoryId,Status,DurationHours,SLA_Target_Hrs,StartDate,EndDate" };
        for (var i = 1; i <= rowCount; i++)
        {
            var categoryId = (i % 3) + 1;
            var status = i % 5 == 0 ? "Failed" : "Success";
            var duration = 1.5m + (i % 24);
            var sla = 10;
            var start = new DateTime(2026, 1, 1).AddHours(i);
            var end = start.AddHours((double)duration);

            lines.Add(string.Create(CultureInfo.InvariantCulture, $"P{i:0000},{categoryId},{status},{duration:0.##},{sla},{start:O},{end:O}"));
        }

        File.WriteAllLines(factPath, lines, Encoding.UTF8);
        return destinationDataDirectory;
    }
}

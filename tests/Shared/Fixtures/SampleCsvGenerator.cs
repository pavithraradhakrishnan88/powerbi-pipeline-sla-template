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

        var categories = new[] { "Ingestion", "Transformation", "Delivery" };
        var environments = new[] { "Dev", "Test", "Prod" };
        var owners = new[] { "Analytics", "Platform", "Operations" };
        var regions = new[] { "East US", "West Europe", "Southeast Asia" };
        var lines = new List<string> { "PipelineID,PipelineName,Category,Status,ScheduledStart,ActualStart,ScheduledEnd,ActualEnd,SLAHours,DurationHours,StartDelayMinutes,EndDelayMinutes,SLAStatus,Environment,Owner,Region,RetryCount" };
        for (var i = 1; i <= rowCount; i++)
        {
            var category = categories[(i - 1) % categories.Length];
            var status = i % 5 == 0 ? "Failed" : "Success";
            var slaHours = 2.0m + (i % 6);
            var durationHours = 1.5m + (i % 24);
            var scheduledStart = new DateTime(2026, 1, 1, 6, 0, 0).AddHours(i);
            var startDelayMinutes = i % 30;
            var actualStart = scheduledStart.AddMinutes(startDelayMinutes);
            var scheduledEnd = scheduledStart.AddHours((double)slaHours);
            var endDelayMinutes = (i * 2) % 25;
            var actualEnd = actualStart.AddHours((double)durationHours).AddMinutes(endDelayMinutes);
            var slaStatus = actualEnd > scheduledEnd ? "Missed" : "Met";
            var environment = environments[(i - 1) % environments.Length];
            var owner = owners[(i - 1) % owners.Length];
            var region = regions[(i - 1) % regions.Length];
            var retryCount = i % 4;

            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{1000 + i},Pipeline_{i:0000},{category},{status},{scheduledStart:yyyy-MM-dd HH:mm:ss},{actualStart:yyyy-MM-dd HH:mm:ss},{scheduledEnd:yyyy-MM-dd HH:mm:ss},{actualEnd:yyyy-MM-dd HH:mm:ss},{slaHours:0.##},{durationHours:0.##},{startDelayMinutes},{endDelayMinutes},{slaStatus},{environment},{owner},{region},{retryCount}"));
        }

        File.WriteAllLines(factPath, lines, Encoding.UTF8);
        return destinationDataDirectory;
    }
}

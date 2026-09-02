using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;
using Xunit;

namespace PowerBiPipelineSlaTemplate.IntegrationTests;

public class PbipArchivePackagingTests
{
    [Fact]
    public void NewPbipArchive_ShouldPreserveRequiredHiddenAndPortablePbipFiles()
    {
        using var workspace = new TemporaryWorkspace();
        var sourceRoot = workspace.CreateDirectory("artifact");
        CreateFile(sourceRoot, "Pipeline_SLA_Tracker.pbip", "{}");
        CreateFile(sourceRoot, Path.Combine("Pipeline_SLA_Tracker.Report", "definition.pbir"), "{}");
        CreateFile(sourceRoot, Path.Combine("Pipeline_SLA_Tracker.Report", ".platform"), "report-platform");
        CreateFile(sourceRoot, Path.Combine("Pipeline_SLA_Tracker.SemanticModel", "definition.pbism"), "{}");
        CreateFile(sourceRoot, Path.Combine("Pipeline_SLA_Tracker.SemanticModel", "definition", "model.tmdl"), "model Model");
        CreateFile(sourceRoot, Path.Combine("Pipeline_SLA_Tracker.SemanticModel", ".platform"), "semantic-platform");
        CreateFile(sourceRoot, Path.Combine("data", "Fact_Pipeline_SampleData.csv"), "Id");
        CreateFile(sourceRoot, Path.Combine("data", "Dim_Category.csv"), "CategoryName");

        var zipPath = workspace.GetPath("DesktopValidation-PBIP.zip");
        var repoRoot = WorkspacePaths.FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "build", "New-PbipArchive.ps1");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments =
                $"-NoLogo -File \"{scriptPath}\" " +
                $"-SourceRoot \"{sourceRoot}\" " +
                $"-DestinationPath \"{zipPath}\" " +
                "-RequiredEntries " +
                "\"Pipeline_SLA_Tracker.pbip\",\"Pipeline_SLA_Tracker.Report/definition.pbir\",\"Pipeline_SLA_Tracker.Report/.platform\",\"Pipeline_SLA_Tracker.SemanticModel/definition.pbism\",\"Pipeline_SLA_Tracker.SemanticModel/definition/model.tmdl\",\"Pipeline_SLA_Tracker.SemanticModel/.platform\",\"data/Fact_Pipeline_SampleData.csv\",\"data/Dim_Category.csv\"",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        process.Should().NotBeNull();
        process!.WaitForExit();

        var output = process.StandardOutput.ReadToEnd() + Environment.NewLine + process.StandardError.ReadToEnd();
        process.ExitCode.Should().Be(0, output);
        File.Exists(zipPath).Should().BeTrue();

        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        entries.Should().Contain("Pipeline_SLA_Tracker.pbip");
        entries.Should().Contain("Pipeline_SLA_Tracker.Report/definition.pbir");
        entries.Should().Contain("Pipeline_SLA_Tracker.Report/.platform");
        entries.Should().Contain("Pipeline_SLA_Tracker.SemanticModel/definition.pbism");
        entries.Should().Contain("Pipeline_SLA_Tracker.SemanticModel/definition/model.tmdl");
        entries.Should().Contain("Pipeline_SLA_Tracker.SemanticModel/.platform");
        entries.Should().Contain("data/Fact_Pipeline_SampleData.csv");
        entries.Should().Contain("data/Dim_Category.csv");
    }

    private static void CreateFile(string root, string relativePath, string contents)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
    }
}

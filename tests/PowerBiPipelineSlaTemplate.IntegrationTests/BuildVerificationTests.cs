using System;
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;
using Xunit;

namespace PowerBiPipelineSlaTemplate.IntegrationTests;

public class BuildVerificationTests
{
    [Fact]
    public void DotnetBuild_ShouldSucceed_ForCoreProject()
    {
        var repoRoot = WorkspacePaths.FindRepoRoot();
        var csprojPath = Path.Combine(repoRoot, "src", "PowerBiPipelineSlaTemplate.Core", "PowerBiPipelineSlaTemplate.Core.csproj");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{csprojPath}\" -v minimal",
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
    }
}

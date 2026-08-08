using System;
using System.IO;
using PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;

namespace PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;

public sealed class TemporaryWorkspace : IDisposable
{
    public TemporaryWorkspace()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "pbip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);

        try
        {
            var repoRoot = WorkspacePaths.FindRepoRoot();
            var sourceMeasureDefinitionsPath = Path.Combine(repoRoot, "scripts", "metadata", "MeasureDefinitions.json");
            if (File.Exists(sourceMeasureDefinitionsPath))
            {
                var destinationMeasureDefinitionsPath = Path.Combine(RootPath, "scripts", "metadata", "MeasureDefinitions.json");
                Directory.CreateDirectory(Path.GetDirectoryName(destinationMeasureDefinitionsPath)!);
                File.Copy(sourceMeasureDefinitionsPath, destinationMeasureDefinitionsPath, overwrite: true);
            }
        }
        catch (Exception)
        {
            // Best-effort test fixture setup; the tests only require the file when present.
        }
    }

    public string RootPath { get; }

    public string CreateDirectory(string relativePath)
    {
        var fullPath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public string GetPath(string relativePath)
    {
        return Path.Combine(RootPath, relativePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup for test temp directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup for test temp directory.
        }
    }
}

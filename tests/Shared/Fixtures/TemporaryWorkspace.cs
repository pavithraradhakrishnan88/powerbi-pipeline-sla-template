using System;
using System.IO;

namespace PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;

public sealed class TemporaryWorkspace : IDisposable
{
    public TemporaryWorkspace()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "pbip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
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

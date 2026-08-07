using System;
using System.IO;

namespace PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;

public static class WorkspacePaths
{
    public static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "README.md"))
                && Directory.Exists(Path.Combine(current.FullName, "src")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root from test execution directory.");
    }
}

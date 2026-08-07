using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;

public static class GoldenFileComparer
{
    private static readonly Regex GuidRegex = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
    private static readonly Regex TempPathRegex = new(@"[A-Za-z]:\\[^\""\r\n]*pbip-tests[^\""\r\n]*", RegexOptions.Compiled);
    private static readonly Regex TimestampRegex = new("\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(?:\\.\\d+)?(?:Z|[+-]\\d{2}:\\d{2})", RegexOptions.Compiled);

    public static void AssertDirectoryMatches(string expectedRoot, string actualRoot, params string[] relativeFiles)
    {
        foreach (var relativeFile in relativeFiles)
        {
            var expectedPath = Path.Combine(expectedRoot, relativeFile);
            var actualPath = Path.Combine(actualRoot, relativeFile);

            File.Exists(expectedPath).Should().BeTrue($"Expected golden file '{relativeFile}' should exist.");
            File.Exists(actualPath).Should().BeTrue($"Generated file '{relativeFile}' should exist.");

            var expectedNormalized = Normalize(File.ReadAllText(expectedPath));
            var actualNormalized = Normalize(File.ReadAllText(actualPath));

            if (!string.Equals(expectedNormalized, actualNormalized, StringComparison.Ordinal))
            {
                throw new Xunit.Sdk.XunitException(BuildDiffMessage(relativeFile, expectedNormalized, actualNormalized));
            }
        }
    }

    private static string Normalize(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        normalized = GuidRegex.Replace(normalized, "<guid>");
        normalized = TempPathRegex.Replace(normalized, "<temp-path>");
        normalized = TimestampRegex.Replace(normalized, "<timestamp>");
        normalized = normalized.Replace(".tmp-<guid>", ".tmp-<guid>", StringComparison.Ordinal);
        return normalized.Trim();
    }

    private static string BuildDiffMessage(string file, string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var max = Math.Max(expectedLines.Length, actualLines.Length);

        for (var index = 0; index < max; index++)
        {
            var expectedLine = index < expectedLines.Length ? expectedLines[index] : "<missing>";
            var actualLine = index < actualLines.Length ? actualLines[index] : "<missing>";
            if (!string.Equals(expectedLine, actualLine, StringComparison.Ordinal))
            {
                return $"Golden file mismatch in '{file}' at line {index + 1}.{Environment.NewLine}Expected: {expectedLine}{Environment.NewLine}Actual:   {actualLine}";
            }
        }

        return $"Golden file mismatch in '{file}'.";
    }
}

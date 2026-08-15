using System;
using System.IO;
using System.Text;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    /// <summary>
    /// Writes the root-level .pbip project file that Power BI Desktop uses to open a PBIP
    /// project. The semantic model is intentionally not referenced here: the report's own
    /// definition.pbir resolves the semantic model via a relative byPath reference, so the
    /// .pbip only needs to point at the report folder.
    /// </summary>
    public sealed class PbipProjectWriter
    {
        private const string ReportFolderSuffix = ".Report";

        /// <summary>
        /// Writes the .pbip file into <paramref name="pbipRootPath"/>, deriving both the
        /// project name and the file name from the generated report folder.
        /// </summary>
        /// <param name="reportRootPath">The generated report output directory, e.g. ".../BuildResult/PBIP/Pipeline_SLA_Tracker.Report".</param>
        /// <param name="pbipRootPath">The directory the .pbip file should be written into. Must be the parent of both the report and semantic model output directories.</param>
        /// <returns>The full path to the written .pbip file.</returns>
        public string Write(string reportRootPath, string pbipRootPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(pbipRootPath);

            var reportFolderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(reportRootPath));
            if (string.IsNullOrEmpty(reportFolderName) || !reportFolderName.EndsWith(ReportFolderSuffix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Cannot derive a PBIP project name from report folder '{reportRootPath}'; expected it to end with '{ReportFolderSuffix}'.");

            var projectName = reportFolderName[..^ReportFolderSuffix.Length];
            Directory.CreateDirectory(pbipRootPath);
            var pbipFilePath = Path.Combine(pbipRootPath, projectName + ".pbip");

            var json =
                "{\r\n" +
                "  \"version\": \"1.0\",\r\n" +
                "  \"artifacts\": [\r\n" +
                "    {\r\n" +
                "      \"report\": {\r\n" +
                $"        \"path\": \"{reportFolderName}\"\r\n" +
                "      }\r\n" +
                "    }\r\n" +
                "  ],\r\n" +
                "  \"settings\": {\r\n" +
                "    \"enableAutoRecovery\": true\r\n" +
                "  }\r\n" +
                "}";

            File.WriteAllText(pbipFilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return pbipFilePath;
        }
    }
}

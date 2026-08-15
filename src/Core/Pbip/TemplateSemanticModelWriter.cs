using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    /// <summary>
    /// Produces a semantic model by copying the authoritative template wholesale and
    /// applying only explicitly environment-dependent substitutions.
    /// </summary>
    public sealed class TemplateSemanticModelWriter
    {
        private static readonly Regex DataFolderExpressionRegex = new(
            @"(?m)(expression\s+DataFolder\s*=\s*\")([^\"]*)(\"\s+meta\b)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RelationshipRegex = new(
            @"(?m)^\s*relationship\s+\S+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex MeasureRegex = new(
            @"(?m)^\s*measure\s+(?:'[^']+'|[^\r\n=]+)\s*=",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public void Write(
            string templateRootPath,
            string semanticModelRootPath,
            string dataFolderPath,
            Action<string>? logger = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(templateRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);

            var templateRoot = Path.GetFullPath(templateRootPath);
            var outputRoot = Path.GetFullPath(semanticModelRootPath);

            if (!Directory.Exists(templateRoot))
                throw new DirectoryNotFoundException($"Semantic model template directory was not found: {templateRoot}");

            if (string.Equals(templateRoot.TrimEnd(Path.DirectorySeparatorChar), outputRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "SemanticModelTemplateRootPath and SemanticModelRootPath must be different directories. " +
                    "The authoritative template must never be patched in place.");

            if (Directory.Exists(outputRoot))
                Directory.Delete(outputRoot, recursive: true);

            CopyDirectoryRecursively(templateRoot, outputRoot);

            LogDiagnostics(outputRoot, "after-template-copy", logger);

            PatchDataFolder(outputRoot, dataFolderPath);

            LogDiagnostics(outputRoot, "after-datafolder-patch", logger);
        }

        private static void PatchDataFolder(string semanticModelRootPath, string dataFolderPath)
        {
            var expressionsPath = Path.Combine(
                semanticModelRootPath,
                "definition",
                "expressions.tmdl");

            if (!File.Exists(expressionsPath))
                throw new FileNotFoundException("Semantic model template is missing expressions.tmdl.", expressionsPath);

            var expressions = File.ReadAllText(expressionsPath, Encoding.UTF8);
            var matches = DataFolderExpressionRegex.Matches(expressions);

            if (matches.Count != 1)
            {
                throw new InvalidDataException(
                    $"Expected exactly one DataFolder expression in '{expressionsPath}', found {matches.Count}.");
            }

            var match = matches[0];
            var valueStart = match.Groups[2].Index;
            var valueLength = match.Groups[2].Length;
            var replacement = dataFolderPath.Replace("\\", "\\\\");

            var patched = string.Concat(
                expressions.AsSpan(0, valueStart),
                replacement,
                expressions.AsSpan(valueStart + valueLength));

            File.WriteAllText(expressionsPath, patched, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static void LogDiagnostics(
            string semanticModelRootPath,
            string stage,
            Action<string>? logger)
        {
            var definitionPath = Path.Combine(semanticModelRootPath, "definition");
            var tablesPath = Path.Combine(definitionPath, "tables");
            var factPath = Path.Combine(tablesPath, "Fact_Pipeline_SampleData.tmdl");
            var relationshipsPath = Path.Combine(definitionPath, "relationships.tmdl");
            var expressionsPath = Path.Combine(definitionPath, "expressions.tmdl");
            var measuresPath = Path.Combine(tablesPath, "_Measures.tmdl");

            var tableCount = Directory.Exists(tablesPath)
                ? Directory.GetFiles(tablesPath, "*.tmdl", SearchOption.TopDirectoryOnly).Length
                : 0;

            var relationshipCount = File.Exists(relationshipsPath)
                ? RelationshipRegex.Matches(File.ReadAllText(relationshipsPath, Encoding.UTF8)).Count
                : 0;

            var inlineMeasureCount = File.Exists(factPath)
                ? MeasureRegex.Matches(File.ReadAllText(factPath, Encoding.UTF8)).Count
                : 0;

            var expressionCount = File.Exists(expressionsPath)
                ? Regex.Matches(File.ReadAllText(expressionsPath, Encoding.UTF8), @"(?m)^\s*expression\s+", RegexOptions.CultureInvariant).Count
                : 0;

            var dataFolder = "<missing>";
            if (File.Exists(expressionsPath))
            {
                var text = File.ReadAllText(expressionsPath, Encoding.UTF8);
                var match = DataFolderExpressionRegex.Match(text);
                if (match.Success)
                    dataFolder = match.Groups[2].Value;
            }

            logger?.Invoke(
                $"SEMANTIC-MODEL-DIAG|Stage={stage}|Root={semanticModelRootPath}|" +
                $"Tables={tableCount}|InlineMeasuresOnFact={inlineMeasureCount}|" +
                $"Relationships={relationshipCount}|Expressions={expressionCount}|" +
                $"Has_MeasuresTmdl={File.Exists(measuresPath)}|DataFolder={dataFolder}");

            if (stage == "after-template-copy")
            {
                if (File.Exists(measuresPath))
                    throw new InvalidDataException("Authoritative semantic-model template unexpectedly contains _Measures.tmdl.");

                if (inlineMeasureCount != 20)
                    throw new InvalidDataException(
                        $"Authoritative semantic-model template expected 20 inline measures on Fact_Pipeline_SampleData, found {inlineMeasureCount}.");

                if (expressionCount != 2)
                    throw new InvalidDataException(
                        $"Authoritative semantic-model template expected 2 expressions in expressions.tmdl, found {expressionCount}.");
            }
        }

        private static void CopyDirectoryRecursively(string sourceRoot, string destinationRoot)
        {
            Directory.CreateDirectory(destinationRoot);

            foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceRoot, directory);
                Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
            }

            foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceRoot, file);
                var destination = Path.Combine(destinationRoot, relative);
                var destinationDirectory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);
                File.Copy(file, destination, overwrite: true);
            }
        }
    }
}

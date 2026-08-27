using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PowerBiPipelineSlaTemplate.Core;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    public sealed class TemplateSemanticModelWriter
    {
        private static readonly Regex RelationshipRegex = new(@"(?m)^\s*relationship\s+\S+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex MeasureRegex = new(@"(?m)^\s*measure\s+(?:'[^']+'|[^\r\n=]+)\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public void Write(string templateRootPath, string semanticModelRootPath, ModelBuildResult model, Action<string>? logger = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(templateRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRootPath);
            ArgumentNullException.ThrowIfNull(model);
            var templateRoot = Path.GetFullPath(templateRootPath);
            var outputRoot = Path.GetFullPath(semanticModelRootPath);
            if (!Directory.Exists(templateRoot)) throw new DirectoryNotFoundException($"Semantic model template directory was not found: {templateRoot}");
            if (string.Equals(templateRoot.TrimEnd(Path.DirectorySeparatorChar), outputRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("SemanticModelTemplateRootPath and SemanticModelRootPath must be different directories. The authoritative template must never be patched in place.");
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
            CopyDirectoryRecursively(templateRoot, outputRoot);
            LogDiagnostics(outputRoot, "after-template-copy", logger);

            var repositoryRootPath = FindRepositoryRoot(templateRoot);
            TemplateSemanticModelMetadataPatcher.Patch(model, outputRoot, repositoryRootPath, logger);
            NormalizeGeneratedTmdl(outputRoot, logger);

            // Preserve the authoritative template's partition/source expressions exactly.
            // The last-known-good template does not use a DataFolder parameter, so this
            // writer deliberately does not inject, rewrite, or require expressions.tmdl.
            LogDiagnostics(outputRoot, "after-template-metadata-patch", logger);
        }

        private static void NormalizeGeneratedTmdl(string semanticModelRootPath, Action<string>? logger)
        {
            var definition = Path.Combine(semanticModelRootPath, "definition");
            if (!Directory.Exists(definition)) return;

            foreach (var file in Directory.GetFiles(definition, "*.tmdl", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

                // Power BI Desktop 2.156.951.0 reports InvalidLineType/Empty for
                // empty structural lines immediately before partition declarations.
                // Normalize only that invalid boundary; do not strip arbitrary TMDL
                // whitespace elsewhere in the generated model.
                var normalized = Regex.Replace(
                    text,
                    "(?:\\r?\\n[ \\t]*)+(?=[ \\t]*partition\\s+\\S+\\s*=)",
                    newline,
                    RegexOptions.CultureInvariant);

                if (!string.Equals(text, normalized, StringComparison.Ordinal))
                {
                    File.WriteAllText(file, normalized, new UTF8Encoding(false));
                    logger?.Invoke($"TMDL-NORMALIZE|RemovedInvalidEmptyLineBeforePartition|File={file}");
                }
            }
        }

        private static string FindRepositoryRoot(string startingPath)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(startingPath));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "scripts", "metadata", "MeasureDefinitions.json"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException($"Could not locate repository root containing scripts/metadata/MeasureDefinitions.json above template path '{startingPath}'.");
        }

        private static void LogDiagnostics(string root, string stage, Action<string>? logger)
        {
            var definition = Path.Combine(root, "definition"); var tables = Path.Combine(definition, "tables"); var fact = Path.Combine(tables, "Fact_Pipeline_SampleData.tmdl"); var relationships = Path.Combine(definition, "relationships.tmdl"); var expressions = Path.Combine(definition, "expressions.tmdl"); var measures = Path.Combine(tables, "_Measures.tmdl");
            var tableCount = Directory.Exists(tables) ? Directory.GetFiles(tables, "*.tmdl", SearchOption.TopDirectoryOnly).Length : 0; var relationshipCount = File.Exists(relationships) ? RelationshipRegex.Matches(File.ReadAllText(relationships, Encoding.UTF8)).Count : 0; var inlineMeasureCount = File.Exists(fact) ? MeasureRegex.Matches(File.ReadAllText(fact, Encoding.UTF8)).Count : 0; var expressionCount = File.Exists(expressions) ? Regex.Matches(File.ReadAllText(expressions, Encoding.UTF8), @"(?m)^\s*expression\s+").Count : 0;
            logger?.Invoke($"SEMANTIC-MODEL-DIAG|Stage={stage}|Tables={tableCount}|InlineMeasuresOnFact={inlineMeasureCount}|Relationships={relationshipCount}|Expressions={expressionCount}|Has_MeasuresTmdl={File.Exists(measures)}");
        }

        private static void CopyDirectoryRecursively(string source, string destination) { Directory.CreateDirectory(destination); foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir))); foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) { var dest = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, true); } }
    }
}

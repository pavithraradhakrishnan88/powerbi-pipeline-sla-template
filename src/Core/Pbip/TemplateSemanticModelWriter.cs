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

        public void Write(string templateRootPath, string semanticModelRootPath, string dataFolderPath, ModelBuildResult model, Action<string>? logger = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(templateRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
            ArgumentNullException.ThrowIfNull(model);
            var templateRoot = Path.GetFullPath(templateRootPath);
            var outputRoot = Path.GetFullPath(semanticModelRootPath);
            var absoluteDataFolder = Path.GetFullPath(dataFolderPath);
            if (!Directory.Exists(templateRoot)) throw new DirectoryNotFoundException($"Semantic model template directory was not found: {templateRoot}");
            if (!Directory.Exists(absoluteDataFolder)) throw new DirectoryNotFoundException($"Build data directory was not found: {absoluteDataFolder}");
            if (string.Equals(templateRoot.TrimEnd(Path.DirectorySeparatorChar), outputRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("SemanticModelTemplateRootPath and SemanticModelRootPath must be different directories. The authoritative template must never be patched in place.");
            if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
            CopyDirectoryRecursively(templateRoot, outputRoot);
            LogDiagnostics(outputRoot, "after-template-copy", logger);

            var repositoryRootPath = FindRepositoryRoot(templateRoot);
            TemplateSemanticModelMetadataPatcher.Patch(model, outputRoot, repositoryRootPath, logger);

            // The generated/Desktop artifact is environment-specific. Replace every partition
            // reference with the build-time absolute data path, then remove the DataFolder
            // expression entirely so Power BI Desktop does not expose an Edit Parameters dialog.
            PatchDataFolderReferences(outputRoot, absoluteDataFolder);
            var expressionsPath = Path.Combine(outputRoot, "definition", "expressions.tmdl");
            if (File.Exists(expressionsPath)) File.Delete(expressionsPath);

            LogDiagnostics(outputRoot, "after-absolute-data-path-patch", logger, absoluteDataFolder);
        }

        private static void PatchDataFolderReferences(string semanticModelRootPath, string absoluteDataFolder)
        {
            var tablesPath = Path.Combine(semanticModelRootPath, "definition", "tables");
            if (!Directory.Exists(tablesPath)) return;
            var normalizedFolder = absoluteDataFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var file in Directory.GetFiles(tablesPath, "*.tmdl", SearchOption.TopDirectoryOnly))
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                var patched = Regex.Replace(
                    text,
                    @"File\.Contents\(DataFolder\s*&\s*""\\?(?<file>[^""]+)""\)",
                    match => $"File.Contents(\"{normalizedFolder}\\{match.Groups["file"].Value}\")",
                    RegexOptions.CultureInvariant);
                if (!string.Equals(text, patched, StringComparison.Ordinal)) File.WriteAllText(file, patched, new UTF8Encoding(false));
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

        internal static byte[] PatchDataFolderBytes(byte[] expressions, string dataFolderPath)
        {
            ArgumentNullException.ThrowIfNull(expressions); ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
            var encoding = new UTF8Encoding(false, true); var preambleLength = HasUtf8Preamble(expressions) ? 3 : 0;
            encoding.GetString(expressions, preambleLength, expressions.Length - preambleLength);
            var (valueStart, valueLength) = FindDataFolderValueSpan(expressions, preambleLength); var replacement = encoding.GetBytes(dataFolderPath); var replacementEnd = valueStart + valueLength;
            var patched = new byte[expressions.Length - valueLength + replacement.Length]; Buffer.BlockCopy(expressions, 0, patched, 0, valueStart); Buffer.BlockCopy(replacement, 0, patched, valueStart, replacement.Length); Buffer.BlockCopy(expressions, replacementEnd, patched, valueStart + replacement.Length, expressions.Length - replacementEnd); return patched;
        }

        private static (int ValueStart, int ValueLength) FindDataFolderValueSpan(byte[] bytes, int searchStart)
        {
            const string ExpressionKeyword = "expression"; const string DataFolderKeyword = "DataFolder"; const string MetaKeyword = "meta"; var count = 0; var valueStart = -1; var valueLength = -1;
            for (var i = searchStart; i < bytes.Length; i++)
            {
                if (!MatchesAsciiToken(bytes, i, ExpressionKeyword)) continue; var position = SkipAsciiWhitespace(bytes, i + ExpressionKeyword.Length); if (!MatchesAsciiToken(bytes, position, DataFolderKeyword)) continue; position = SkipAsciiWhitespace(bytes, position + DataFolderKeyword.Length); if (position >= bytes.Length || bytes[position] != (byte)'=') continue; position = SkipAsciiWhitespace(bytes, position + 1); if (position >= bytes.Length || bytes[position] != (byte)'"') continue;
                var candidateStart = position + 1; var candidateEnd = candidateStart; while (candidateEnd < bytes.Length && bytes[candidateEnd] != (byte)'"') candidateEnd++; if (candidateEnd >= bytes.Length) throw new InvalidDataException("DataFolder expression contains an unterminated quoted value."); position = SkipAsciiWhitespace(bytes, candidateEnd + 1); if (!MatchesAsciiToken(bytes, position, MetaKeyword)) continue; count++; valueStart = candidateStart; valueLength = candidateEnd - candidateStart;
            }
            if (count != 1) throw new InvalidDataException($"Expected exactly one DataFolder expression in expressions.tmdl, found {count}."); return (valueStart, valueLength);
        }
        private static bool MatchesAsciiToken(byte[] bytes, int start, string token) { if (start < 0 || start + token.Length > bytes.Length) return false; for (var i = 0; i < token.Length; i++) if (bytes[start + i] != (byte)token[i]) return false; if (start > 0 && IsAsciiIdentifierChar(bytes[start - 1])) return false; var end = start + token.Length; return end == bytes.Length || !IsAsciiIdentifierChar(bytes[end]); }
        private static int SkipAsciiWhitespace(byte[] bytes, int start) { var position = start; while (position < bytes.Length && bytes[position] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') position++; return position; }
        private static bool IsAsciiIdentifierChar(byte value) => value is >= (byte)'A' and <= (byte)'Z' || value is >= (byte)'a' and <= (byte)'z' || value is >= (byte)'0' and <= (byte)'9' || value == (byte)'_';
        private static bool HasUtf8Preamble(byte[] bytes) => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        private static void LogDiagnostics(string root, string stage, Action<string>? logger, string? expectedDataFolder = null)
        {
            var definition = Path.Combine(root, "definition"); var tables = Path.Combine(definition, "tables"); var fact = Path.Combine(tables, "Fact_Pipeline_SampleData.tmdl"); var relationships = Path.Combine(definition, "relationships.tmdl"); var expressions = Path.Combine(definition, "expressions.tmdl"); var measures = Path.Combine(tables, "_Measures.tmdl");
            var tableCount = Directory.Exists(tables) ? Directory.GetFiles(tables, "*.tmdl", SearchOption.TopDirectoryOnly).Length : 0; var relationshipCount = File.Exists(relationships) ? RelationshipRegex.Matches(File.ReadAllText(relationships, Encoding.UTF8)).Count : 0; var inlineMeasureCount = File.Exists(fact) ? MeasureRegex.Matches(File.ReadAllText(fact, Encoding.UTF8)).Count : 0; var expressionCount = File.Exists(expressions) ? Regex.Matches(File.ReadAllText(expressions, Encoding.UTF8), @"(?m)^\s*expression\s+").Count : 0;
            logger?.Invoke($"SEMANTIC-MODEL-DIAG|Stage={stage}|Tables={tableCount}|InlineMeasuresOnFact={inlineMeasureCount}|Relationships={relationshipCount}|Expressions={expressionCount}|Has_MeasuresTmdl={File.Exists(measures)}"); if (!string.IsNullOrWhiteSpace(expectedDataFolder)) logger?.Invoke($"SEMANTIC-MODEL-DIAG|ExpectedDataFolder={expectedDataFolder}");
        }
        private static void CopyDirectoryRecursively(string source, string destination) { Directory.CreateDirectory(destination); foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir))); foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) { var dest = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, true); } }
    }
}

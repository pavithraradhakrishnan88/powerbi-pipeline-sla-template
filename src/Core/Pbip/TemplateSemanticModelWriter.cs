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
            @"(?m)(expression\s+DataFolder\s*=\s*"")([^""]*)(""\s+meta\b)",
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

            LogDiagnostics(outputRoot, "after-datafolder-patch", logger, dataFolderPath);
        }

        private static void PatchDataFolder(string semanticModelRootPath, string dataFolderPath)
        {
            var expressionsPath = Path.Combine(
                semanticModelRootPath,
                "definition",
                "expressions.tmdl");

            if (!File.Exists(expressionsPath))
                throw new FileNotFoundException("Semantic model template is missing expressions.tmdl.", expressionsPath);

            // Patch the authoritative file as bytes. The valueStart/valueLength span is therefore
            // measured in actual file bytes, preserving CRLF, UTF-8 BOM state, and all other bytes.
            var expressions = File.ReadAllBytes(expressionsPath);
            var patched = PatchDataFolderBytes(expressions, dataFolderPath);

            File.WriteAllBytes(expressionsPath, patched);
        }

        internal static byte[] PatchDataFolderBytes(byte[] expressions, string dataFolderPath)
        {
            ArgumentNullException.ThrowIfNull(expressions);
            ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var preambleLength = HasUtf8Preamble(expressions) ? 3 : 0;

            // Validate that the source is actually UTF-8, but never use the decoded string to
            // calculate the patch offsets.
            encoding.GetString(expressions, preambleLength, expressions.Length - preambleLength);

            var (valueStart, valueLength) = FindDataFolderValueSpan(expressions, preambleLength);
            var replacement = encoding.GetBytes(dataFolderPath);
            var replacementStart = valueStart;
            var replacementEnd = valueStart + valueLength;

            var patched = new byte[
                expressions.Length - valueLength + replacement.Length];

            Buffer.BlockCopy(expressions, 0, patched, 0, replacementStart);
            Buffer.BlockCopy(replacement, 0, patched, replacementStart, replacement.Length);
            Buffer.BlockCopy(
                expressions,
                replacementEnd,
                patched,
                replacementStart + replacement.Length,
                expressions.Length - replacementEnd);

            return patched;
        }

        private static (int ValueStart, int ValueLength) FindDataFolderValueSpan(
            byte[] bytes,
            int searchStart)
        {
            const string ExpressionKeyword = "expression";
            const string DataFolderKeyword = "DataFolder";
            const string MetaKeyword = "meta";

            var matchCount = 0;
            var valueStart = -1;
            var valueLength = -1;

            for (var i = searchStart; i < bytes.Length; i++)
            {
                if (!MatchesAsciiToken(bytes, i, ExpressionKeyword))
                    continue;

                var position = i + ExpressionKeyword.Length;
                position = SkipAsciiWhitespace(bytes, position);

                if (!MatchesAsciiToken(bytes, position, DataFolderKeyword))
                    continue;

                position += DataFolderKeyword.Length;
                position = SkipAsciiWhitespace(bytes, position);

                if (position >= bytes.Length || bytes[position] != (byte)'=')
                    continue;

                position++;
                position = SkipAsciiWhitespace(bytes, position);

                if (position >= bytes.Length || bytes[position] != (byte)'\"')
                    continue;

                var candidateStart = position + 1;
                var candidateEnd = candidateStart;
                while (candidateEnd < bytes.Length && bytes[candidateEnd] != (byte)'\"')
                    candidateEnd++;

                if (candidateEnd >= bytes.Length)
                {
                    throw new InvalidDataException(
                        "DataFolder expression contains an unterminated quoted value.");
                }

                position = SkipAsciiWhitespace(bytes, candidateEnd + 1);
                if (!MatchesAsciiToken(bytes, position, MetaKeyword))
                    continue;

                matchCount++;
                valueStart = candidateStart;
                valueLength = candidateEnd - candidateStart;
            }

            if (matchCount != 1)
            {
                throw new InvalidDataException(
                    $"Expected exactly one DataFolder expression in expressions.tmdl, found {matchCount}.");
            }

            return (valueStart, valueLength);
        }

        private static bool MatchesAsciiToken(byte[] bytes, int start, string token)
        {
            if (start < 0 || start + token.Length > bytes.Length)
                return false;

            for (var i = 0; i < token.Length; i++)
            {
                if (bytes[start + i] != (byte)token[i])
                    return false;
            }

            if (start > 0 && IsAsciiIdentifierChar(bytes[start - 1]))
                return false;

            var end = start + token.Length;
            return end == bytes.Length || !IsAsciiIdentifierChar(bytes[end]);
        }

        private static int SkipAsciiWhitespace(byte[] bytes, int start)
        {
            var position = start;
            while (position < bytes.Length)
            {
                var value = bytes[position];
                if (value != (byte)' ' && value != (byte)'\t' && value != (byte)'\r' && value != (byte)'\n')
                    break;
                position++;
            }

            return position;
        }

        private static bool IsAsciiIdentifierChar(byte value) =>
            value is >= (byte)'A' and <= (byte)'Z' ||
            value is >= (byte)'a' and <= (byte)'z' ||
            value is >= (byte)'0' and <= (byte)'9' ||
            value == (byte)'_';

        private static bool HasUtf8Preamble(byte[] bytes) =>
            bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF;

        private static void LogDiagnostics(
            string semanticModelRootPath,
            string stage,
            Action<string>? logger,
            string? expectedDataFolder = null)
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

            if (stage == "after-datafolder-patch")
            {
                if (expectedDataFolder is null)
                    throw new InvalidDataException("Expected DataFolder value was not supplied to the post-patch diagnostic.");

                if (dataFolder == "<missing>" ||
                    dataFolder.Contains("\\\\", StringComparison.Ordinal) ||
                    !string.Equals(dataFolder, expectedDataFolder, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Patched DataFolder value is invalid. Expected '{expectedDataFolder}', actual '{dataFolder}'.");
                }
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

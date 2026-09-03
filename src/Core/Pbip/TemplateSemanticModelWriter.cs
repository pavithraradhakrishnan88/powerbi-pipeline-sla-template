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
            LogScheduledStartRelationshipDiagnostics(outputRoot, logger);
            var repositoryRootPath = FindRepositoryRoot(templateRoot);
            LogScheduledStartRelationshipDiagnostics(outputRoot, logger);
            TemplateSemanticModelMetadataPatcher.Patch(model, outputRoot, repositoryRootPath, logger);
            NormalizeGeneratedTmdl(outputRoot, logger);
            LogDiagnostics(outputRoot, "after-template-metadata-patch", logger);
        }

        private static void LogScheduledStartRelationshipDiagnostics(string root, Action<string>? logger)
        {
            var path = Path.Combine(root, "definition", "relationships.tmdl");
            var exists = File.Exists(path);
            var size = exists ? new FileInfo(path).Length : 0L;
            var text = exists ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
            var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "CRLF" : text.Contains("\n", StringComparison.Ordinal) ? "LF" : "NONE";
            var relationshipId = "db2083da-0a18-4176-ab57-a096ce539554";
            var localDateTable = "LocalDateTable_9043e032-67a4-45e7-bf88-28fec57966b9";
            var pattern = $"relationship\\s+{Regex.Escape(relationshipId)}\\s+\\r?\\n\\s*joinOnDateBehavior:\\s*datePartOnly\\s+\\r?\\n\\s*fromColumn:\\s*Fact_Pipeline_SampleData\\.ScheduledStart\\s+\\r?\\n\\s*toColumn:\\s*{Regex.Escape(localDateTable)}\\.Date";
            var match = exists && Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant);
            var start = text.IndexOf($"relationship {relationshipId}", StringComparison.Ordinal);
            var end = start >= 0 ? text.IndexOf("relationship ", start + 1, StringComparison.Ordinal) : -1;
            var block = start >= 0 ? text[start..(end >= 0 ? end : text.Length)].TrimEnd('\r', '\n') : "<NOT FOUND>";
            Console.WriteLine($"TMDL-RELATIONSHIP-DIAGNOSTIC|Path={path}|Exists={exists}|Size={size}|Newline={newline}");
            Console.WriteLine($"TMDL-RELATIONSHIP-DIAGNOSTIC|ScheduledStartBlock={block.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}");
            Console.WriteLine($"TMDL-RELATIONSHIP-DIAGNOSTIC|Pattern={pattern}");
            Console.WriteLine($"TMDL-RELATIONSHIP-DIAGNOSTIC|IsMatch={match}");
        }

        private static void NormalizeGeneratedTmdl(string semanticModelRootPath, Action<string>? logger)
        {
            var definition = Path.Combine(semanticModelRootPath, "definition");
            if (!Directory.Exists(definition)) return;

            foreach (var file in Directory.GetFiles(definition, "*.tmdl", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file, Encoding.UTF8);
                var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

                // Remove blank lines immediately before partitions without consuming
                // the partition declaration's leading whitespace. That whitespace is
                // the table-child indentation and must remain intact.
                var normalized = Regex.Replace(
                    text,
                    @"(?m)(?:^[ \t]*\r?\n)+(?=[ \t]*partition\s+\S+\s*=)",
                    string.Empty,
                    RegexOptions.CultureInvariant);

                // TMDL treats an M partition source as a multi-line expression.
                // The expression must be one indentation level deeper than the
                // `source =` property and every line of that expression must remain
                // inside that indentation boundary. The previous writer emitted
                // `let` at level 4 but the M body at level 4 + spaces, which Desktop
                // 2.156 parsed as a new/invalid TMDL line type.
                normalized = NormalizePartitionExpressions(normalized, newline, file, logger);

                if (!string.Equals(text, normalized, StringComparison.Ordinal))
                {
                    File.WriteAllText(file, normalized, new UTF8Encoding(false));
                    logger?.Invoke($"TMDL-NORMALIZE|RewrotePartitionExpressions|File={file}");
                }
            }
        }

        private static string NormalizePartitionExpressions(string text, string newline, string file, Action<string>? logger)
        {
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Split('\n');
            var changed = false;

            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^[ \t]*partition\s+\S+\s*=\s*m\s*$", RegexOptions.CultureInvariant))
                    continue;

                var partitionIndent = Regex.Match(lines[i], @"^[ \t]*", RegexOptions.CultureInvariant).Value;
                var partitionChildIndent = partitionIndent + "\t";
                var sourceIndex = -1;
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var candidate = lines[j].Trim();
                    var candidateIndent = Regex.Match(lines[j], @"^[ \t]*", RegexOptions.CultureInvariant).Value;
                    if (Regex.IsMatch(candidate, @"^source\s*=", RegexOptions.CultureInvariant))
                    {
                        sourceIndex = j;
                        break;
                    }

                    if (candidateIndent.Length <= partitionIndent.Length &&
                        Regex.IsMatch(candidate, @"^(?:partition|column|measure|hierarchy|calculationGroup|annotation)\b", RegexOptions.CultureInvariant))
                        break;
                }

                if (sourceIndex < 0 || sourceIndex + 1 >= lines.Length)
                    continue;

                var normalizedSource = partitionChildIndent + lines[sourceIndex].Trim();
                if (!string.Equals(lines[sourceIndex], normalizedSource, StringComparison.Ordinal))
                {
                    lines[sourceIndex] = normalizedSource;
                    changed = true;
                }

                if (!string.Equals(lines[sourceIndex].Trim(), "source =", StringComparison.Ordinal))
                {
                    continue;
                }

                var expressionStart = sourceIndex + 1;
                var expressionEnd = expressionStart;
                while (expressionEnd < lines.Length)
                {
                    var line = lines[expressionEnd];
                    var trimmed = line.Trim();
                    var indent = Regex.Match(line, @"^[ \t]*", RegexOptions.CultureInvariant).Value;
                    if (trimmed.Length > 0 &&
                        indent.Length <= partitionIndent.Length &&
                        Regex.IsMatch(trimmed, @"^(?:partition|column|measure|hierarchy|calculationGroup|annotation)\b", RegexOptions.CultureInvariant))
                        break;
                    expressionEnd++;
                }

                // Only normalize an actual multi-line M expression. A source property
                // followed by another table child is left untouched so we don't alter
                // unrelated TMDL structures.
                if (expressionEnd <= expressionStart)
                    continue;

                var expressionLines = lines[expressionStart..expressionEnd];
                var firstNonBlank = Array.FindIndex(expressionLines, l => l.Trim().Length > 0);
                if (firstNonBlank < 0)
                    continue;

                var baseIndent = partitionChildIndent + "\t";
                var nestedIndent = baseIndent + "\t";
                var normalizedExpression = new string[expressionLines.Length];
                for (var k = 0; k < expressionLines.Length; k++)
                {
                    var original = expressionLines[k];
                    if (original.Trim().Length == 0)
                    {
                        normalizedExpression[k] = string.Empty;
                        continue;
                    }

                    var content = original.TrimStart(' ', '\t');
                    normalizedExpression[k] = string.Equals(content, "let", StringComparison.Ordinal) ||
                                              string.Equals(content, "in", StringComparison.Ordinal)
                        ? baseIndent + content
                        : nestedIndent + content;
                }

                for (var k = 0; k < expressionLines.Length; k++)
                {
                    if (!string.Equals(expressionLines[k], normalizedExpression[k], StringComparison.Ordinal))
                    {
                        lines[expressionStart + k] = normalizedExpression[k];
                        changed = true;
                    }
                }

                logger?.Invoke($"TMDL-NORMALIZE|PartitionExpressionIndentation|File={file}|PartitionLine={i + 1}");
                i = expressionEnd - 1;
            }

            return changed ? string.Join(newline, lines) : text;
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

        private static void CopyDirectoryRecursively(string source, string destination) { Directory.CreateDirectory(destination); foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.OrdinalIgnoreCase)); foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) { var target = file.Replace(source, destination, StringComparison.OrdinalIgnoreCase); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true); } }

        private static void LogDiagnostics(string root, string stage, Action<string>? logger)
        {
            var definition = Path.Combine(root, "definition"); var tables = Path.Combine(definition, "tables"); var fact = Path.Combine(tables, "Fact_Pipeline_SampleData.tmdl"); var relationships = Path.Combine(definition, "relationships.tmdl"); var expressions = Path.Combine(definition, "expressions.tmdl"); var measures = Path.Combine(tables, "_Measures.tmdl");
            var tableCount = Directory.Exists(tables) ? Directory.GetFiles(tables, "*.tmdl", SearchOption.TopDirectoryOnly).Length : 0; var relationshipCount = File.Exists(relationships) ? RelationshipRegex.Matches(File.ReadAllText(relationships, Encoding.UTF8)).Count : 0; var inlineMeasureCount = File.Exists(fact) ? MeasureRegex.Matches(File.ReadAllText(fact, Encoding.UTF8)).Count : 0; var expressionCount = File.Exists(expressions) ? Regex.Matches(File.ReadAllText(expressions, Encoding.UTF8), @"(?m)^\s*expression\s+").Count : 0;
            logger?.Invoke($"SEMANTIC-MODEL-DIAG|Stage={stage}|Tables={tableCount}|InlineMeasuresOnFact={inlineMeasureCount}|Relationships={relationshipCount}|Expressions={expressionCount}|Has_MeasuresTmdl={File.Exists(measures)}");
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    public sealed class PbirReportWriter
    {
        private const string DefinitionPbirVersion = "4.0";
        private const string ReportJsonVersion = "1.0";
        private const string PageSchemaUrl = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/page/2.1.0/schema.json";
        private const string VisualContainerSchemaUrl = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/visualContainer/2.10.0/schema.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public void WriteFromJson(string reportDefinitionJson, string reportRootPath, string semanticModelRelativePath, string? themeSourceRootPath = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reportDefinitionJson);
            var document = JsonSerializer.Deserialize<ReportDefinitionDocument>(reportDefinitionJson, JsonOptions);
            if (document is null) throw new InvalidOperationException("Unable to deserialize report definition JSON.");
            Write(document, reportRootPath, semanticModelRelativePath, themeSourceRootPath);
        }

        public void Write(ReportDefinitionDocument document, string reportRootPath, string semanticModelRelativePath, string? themeSourceRootPath = null)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentException.ThrowIfNullOrWhiteSpace(reportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRelativePath);
            ValidateReportDefinition(document);

            var parentDirectory = Path.GetDirectoryName(reportRootPath);
            if (string.IsNullOrWhiteSpace(parentDirectory)) throw new InvalidOperationException($"Unable to determine parent directory for '{reportRootPath}'.");
            Directory.CreateDirectory(parentDirectory);

            var stagingPath = Path.Combine(parentDirectory, $"{Path.GetFileName(reportRootPath)}.tmp-{Guid.NewGuid():N}");
            try
            {
                WriteToDirectory(document, stagingPath, semanticModelRelativePath, themeSourceRootPath);
                ReplaceDirectoryAtomically(stagingPath, reportRootPath);
            }
            finally { SafeDeleteDirectory(stagingPath); }
        }

        public void WriteFromTemplate(string templateReportRootPath, string reportRootPath, string semanticModelRelativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(templateReportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(reportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRelativePath);
            if (!Directory.Exists(templateReportRootPath)) throw new InvalidOperationException($"Report template directory '{templateReportRootPath}' does not exist.");

            var parentDirectory = Path.GetDirectoryName(reportRootPath);
            if (string.IsNullOrWhiteSpace(parentDirectory)) throw new InvalidOperationException($"Unable to determine parent directory for '{reportRootPath}'.");
            Directory.CreateDirectory(parentDirectory);

            var stagingPath = Path.Combine(parentDirectory, $"{Path.GetFileName(reportRootPath)}.tmp-{Guid.NewGuid():N}");
            try
            {
                CopyDirectoryRecursively(templateReportRootPath, stagingPath);
                // The template contains native visual definitions. Normalize the writer's
                // output here so visualContainerObjects.title is serialized as an array.
                PbirVisualContainerNormalizer.NormalizeReport(stagingPath);

                File.WriteAllText(Path.Combine(stagingPath, "definition.pbir"), BuildDefinitionPbir(semanticModelRelativePath));
                ValidateTemplateOutput(stagingPath);
                ReplaceDirectoryAtomically(stagingPath, reportRootPath);
            }
            finally { SafeDeleteDirectory(stagingPath); }
        }

        private static void WriteToDirectory(ReportDefinitionDocument document, string outputRootPath, string semanticModelRelativePath, string? themeSourceRootPath)
        {
            Directory.CreateDirectory(outputRootPath);
            var pagesPath = Path.Combine(outputRootPath, "pages");
            var staticResourcesPath = Path.Combine(outputRootPath, "staticResources");
            var registeredResourcesPath = Path.Combine(staticResourcesPath, "RegisteredResources");
            Directory.CreateDirectory(pagesPath);
            Directory.CreateDirectory(staticResourcesPath);
            Directory.CreateDirectory(registeredResourcesPath);

            File.WriteAllText(Path.Combine(outputRootPath, "definition.pbir"), BuildDefinitionPbir(semanticModelRelativePath));
            File.WriteAllText(Path.Combine(outputRootPath, "report.json"), BuildNativeReportJson(document));
            File.WriteAllText(Path.Combine(outputRootPath, "reportExtensions.json"), BuildReportExtensionsJson());
            foreach (var page in document.Pages)
            {
                var pageFolderPath = Path.Combine(pagesPath, SanitizeDirectoryName(ResolvePageId(page)));
                Directory.CreateDirectory(pageFolderPath);
                File.WriteAllText(Path.Combine(pageFolderPath, "page.json"), BuildPageJson(document, page));
            }
            foreach (var theme in document.Themes)
            {
                var sourceThemePath = ResolveThemePath(theme.Path, outputRootPath, themeSourceRootPath);
                if (sourceThemePath is null || !File.Exists(sourceThemePath)) throw new InvalidOperationException($"Theme '{theme.Name}' references missing file '{theme.Path}'.");
                File.Copy(sourceThemePath, Path.Combine(registeredResourcesPath, Path.GetFileName(sourceThemePath)), true);
            }
            CopyAdditionalStaticResources(registeredResourcesPath, themeSourceRootPath);
            ValidateGeneratedOutput(outputRootPath, document);
        }

        private static void ReplaceDirectoryAtomically(string stagingPath, string targetPath)
        {
            var backupPath = $"{targetPath}.bak-{Guid.NewGuid():N}";
            var hasExistingTarget = Directory.Exists(targetPath);
            if (hasExistingTarget) Directory.Move(targetPath, backupPath);
            try
            {
                Directory.Move(stagingPath, targetPath);
                if (hasExistingTarget) SafeDeleteDirectory(backupPath);
            }
            catch
            {
                if (Directory.Exists(targetPath)) SafeDeleteDirectory(targetPath);
                if (hasExistingTarget && Directory.Exists(backupPath)) Directory.Move(backupPath, targetPath);
                throw;
            }
        }

        private static void CopyDirectoryRecursively(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(destinationPath);
            foreach (var directoryPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destinationPath, Path.GetRelativePath(sourcePath, directoryPath)));
            foreach (var sourceFilePath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var destinationFilePath = Path.Combine(destinationPath, Path.GetRelativePath(sourcePath, sourceFilePath));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
                File.Copy(sourceFilePath, destinationFilePath, true);
            }
        }

        private static string BuildDefinitionPbir(string semanticModelRelativePath)
        {
            var definitionPbir = new { version = DefinitionPbirVersion, datasetReference = new { byPath = new { path = semanticModelRelativePath.Replace("\\", "/", StringComparison.Ordinal) } } };
            return JsonSerializer.Serialize(definitionPbir, JsonOptions);
        }

        private static string BuildPageJson(ReportDefinitionDocument document, ReportPageDefinition page)
        {
            var pageId = ResolvePageId(page);
            var visualContainers = document.VisualPositions.Where(v => string.Equals(ResolveVisualPageId(v), pageId, StringComparison.OrdinalIgnoreCase)).Select((v, i) => (object)BuildVisualContainerFromPosition(v, i)).ToList();
            visualContainers.AddRange(document.Slicers.Where(s => string.Equals(ResolveSlicerPageId(s), pageId, StringComparison.OrdinalIgnoreCase)).Select((s, i) => (object)BuildVisualContainerFromSlicer(s, i + visualContainers.Count)));
            var pageDocument = new Dictionary<string, object?>
            {
                ["$schema"] = PageSchemaUrl, ["name"] = pageId, ["displayName"] = page.DisplayName,
                ["displayOption"] = "FitToPage", ["height"] = page.Canvas.Height, ["width"] = page.Canvas.Width,
                ["visualContainers"] = visualContainers
            };
            return JsonSerializer.Serialize(pageDocument, JsonOptions);
        }

        private static object BuildVisualContainerFromPosition(VisualPositionDefinition visual, int index) => new Dictionary<string, object?>
        {
            ["$schema"] = VisualContainerSchemaUrl, ["name"] = visual.VisualId,
            ["position"] = new Dictionary<string, object?> { ["x"] = visual.X, ["y"] = visual.Y, ["z"] = 1000 + index, ["height"] = visual.Height, ["width"] = visual.Width, ["tabOrder"] = index + 1 },
            ["visual"] = new Dictionary<string, object?> { ["visualType"] = "shape", ["drillFilterOtherVisuals"] = true }
        };

        private static object BuildVisualContainerFromSlicer(SlicerDefinition slicer, int index) => new Dictionary<string, object?>
        {
            ["$schema"] = VisualContainerSchemaUrl, ["name"] = BuildSlicerVisualName(slicer),
            ["position"] = new Dictionary<string, object?> { ["x"] = slicer.X, ["y"] = slicer.Y, ["z"] = 2000 + index, ["height"] = slicer.Height, ["width"] = slicer.Width, ["tabOrder"] = index + 1 },
            ["visual"] = new Dictionary<string, object?>
            {
                ["visualType"] = "slicer",
                ["query"] = new Dictionary<string, object?> { ["queryState"] = new Dictionary<string, object?> { ["Values"] = new Dictionary<string, object?> { ["projections"] = new[] { new Dictionary<string, object?> { ["queryRef"] = slicer.Field, ["nativeQueryRef"] = slicer.Field, ["active"] = true } } } } },
                ["objects"] = new Dictionary<string, object?> { ["data"] = new[] { new Dictionary<string, object?> { ["properties"] = new Dictionary<string, object?> { ["mode"] = new Dictionary<string, object?> { ["expr"] = new Dictionary<string, object?> { ["Literal"] = new Dictionary<string, object?> { ["Value"] = $"'{slicer.Type}'" } } } } } } },
                ["drillFilterOtherVisuals"] = true
            }
        };

        private static string BuildSlicerVisualName(SlicerDefinition slicer)
        {
            var fieldPart = string.IsNullOrWhiteSpace(slicer.Field) ? "Slicer" : new string(slicer.Field.Where(char.IsLetterOrDigit).ToArray());
            return string.IsNullOrWhiteSpace(fieldPart) ? "Slicer" : $"Slicer{fieldPart}";
        }

        // Existing validation/helpers remain unchanged below this point.

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
            if (document is null)
            {
                throw new InvalidOperationException("Unable to deserialize report definition JSON.");
            }

            Write(document, reportRootPath, semanticModelRelativePath, themeSourceRootPath);
        }

        public void Write(ReportDefinitionDocument document, string reportRootPath, string semanticModelRelativePath, string? themeSourceRootPath = null)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentException.ThrowIfNullOrWhiteSpace(reportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRelativePath);

            ValidateReportDefinition(document);

            var parentDirectory = Path.GetDirectoryName(reportRootPath);
            if (string.IsNullOrWhiteSpace(parentDirectory))
            {
                throw new InvalidOperationException($"Unable to determine parent directory for '{reportRootPath}'.");
            }

            Directory.CreateDirectory(parentDirectory);

            var stagingPath = Path.Combine(parentDirectory, $"{Path.GetFileName(reportRootPath)}.tmp-{Guid.NewGuid():N}");
            try
            {
                WriteToDirectory(document, stagingPath, semanticModelRelativePath, themeSourceRootPath);
                ReplaceDirectoryAtomically(stagingPath, reportRootPath);
            }
            finally
            {
                SafeDeleteDirectory(stagingPath);
            }
        }

        public void WriteFromTemplate(string templateReportRootPath, string reportRootPath, string semanticModelRelativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(templateReportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(reportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRelativePath);

            if (!Directory.Exists(templateReportRootPath))
            {
                throw new InvalidOperationException($"Report template directory '{templateReportRootPath}' does not exist.");
            }

            var parentDirectory = Path.GetDirectoryName(reportRootPath);
            if (string.IsNullOrWhiteSpace(parentDirectory))
            {
                throw new InvalidOperationException($"Unable to determine parent directory for '{reportRootPath}'.");
            }

            Directory.CreateDirectory(parentDirectory);

            var stagingPath = Path.Combine(parentDirectory, $"{Path.GetFileName(reportRootPath)}.tmp-{Guid.NewGuid():N}");
            try
            {
                CopyDirectoryRecursively(templateReportRootPath, stagingPath);

                var definitionPbirPath = Path.Combine(stagingPath, "definition.pbir");
                File.WriteAllText(definitionPbirPath, BuildDefinitionPbir(semanticModelRelativePath));

                ValidateTemplateOutput(stagingPath);
                ReplaceDirectoryAtomically(stagingPath, reportRootPath);
            }
            finally
            {
                SafeDeleteDirectory(stagingPath);
            }
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
                var pageFolderName = SanitizeDirectoryName(ResolvePageId(page));
                var pageFolderPath = Path.Combine(pagesPath, pageFolderName);
                Directory.CreateDirectory(pageFolderPath);

                File.WriteAllText(Path.Combine(pageFolderPath, "page.json"), BuildPageJson(document, page));
            }

            foreach (var theme in document.Themes)
            {
                var sourceThemePath = ResolveThemePath(theme.Path, outputRootPath, themeSourceRootPath);
                if (sourceThemePath is null || !File.Exists(sourceThemePath))
                {
                    throw new InvalidOperationException($"Theme '{theme.Name}' references missing file '{theme.Path}'.");
                }

                var destinationThemePath = Path.Combine(registeredResourcesPath, Path.GetFileName(sourceThemePath));
                File.Copy(sourceThemePath, destinationThemePath, overwrite: true);
            }

            CopyAdditionalStaticResources(registeredResourcesPath, themeSourceRootPath);
            ValidateGeneratedOutput(outputRootPath, document);
        }

        private static void ReplaceDirectoryAtomically(string stagingPath, string targetPath)
        {
            var backupPath = $"{targetPath}.bak-{Guid.NewGuid():N}";
            var hasExistingTarget = Directory.Exists(targetPath);

            if (hasExistingTarget)
            {
                Directory.Move(targetPath, backupPath);
            }

            try
            {
                Directory.Move(stagingPath, targetPath);

                if (hasExistingTarget)
                {
                    SafeDeleteDirectory(backupPath);
                }
            }
            catch
            {
                if (Directory.Exists(targetPath))
                {
                    SafeDeleteDirectory(targetPath);
                }

                if (hasExistingTarget && Directory.Exists(backupPath))
                {
                    Directory.Move(backupPath, targetPath);
                }

                throw;
            }
        }

        private static void CopyDirectoryRecursively(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(destinationPath);

            foreach (var directoryPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourcePath, directoryPath);
                Directory.CreateDirectory(Path.Combine(destinationPath, relativePath));
            }

            foreach (var sourceFilePath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourcePath, sourceFilePath);
                var destinationFilePath = Path.Combine(destinationPath, relativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationFilePath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
            }
        }

        private static string BuildDefinitionPbir(string semanticModelRelativePath)
        {
            var normalizedPath = semanticModelRelativePath.Replace("\\", "/", StringComparison.Ordinal);

            var definitionPbir = new
            {
                version = DefinitionPbirVersion,
                datasetReference = new
                {
                    byPath = new
                    {
                        path = normalizedPath
                    }
                }
            };

            return JsonSerializer.Serialize(definitionPbir, JsonOptions);
        }

        private static string BuildPageJson(ReportDefinitionDocument document, ReportPageDefinition page)
        {
            var pageId = ResolvePageId(page);

            var visualContainers = document.VisualPositions
                .Where(visual => string.Equals(ResolveVisualPageId(visual), pageId, StringComparison.OrdinalIgnoreCase))
                .Select((visual, index) => (object)BuildVisualContainerFromPosition(visual, index))
                .ToList();

            visualContainers.AddRange(document.Slicers
                .Where(slicer => string.Equals(ResolveSlicerPageId(slicer), pageId, StringComparison.OrdinalIgnoreCase))
                .Select((slicer, index) => (object)BuildVisualContainerFromSlicer(slicer, index + visualContainers.Count)));

            var pageDocument = new Dictionary<string, object?>
            {
                ["$schema"] = PageSchemaUrl,
                ["name"] = pageId,
                ["displayName"] = page.DisplayName,
                ["displayOption"] = "FitToPage",
                ["height"] = page.Canvas.Height,
                ["width"] = page.Canvas.Width,
                ["visualContainers"] = visualContainers
            };

            return JsonSerializer.Serialize(
                pageDocument,
                JsonOptions);
        }

        private static object BuildVisualContainerFromPosition(VisualPositionDefinition visual, int index)
        {
            return new Dictionary<string, object?>
            {
                ["$schema"] = VisualContainerSchemaUrl,
                ["name"] = visual.VisualId,
                ["position"] = new Dictionary<string, object?>
                {
                    ["x"] = visual.X,
                    ["y"] = visual.Y,
                    ["z"] = 1000 + index,
                    ["height"] = visual.Height,
                    ["width"] = visual.Width,
                    ["tabOrder"] = index + 1
                },
                ["visual"] = new Dictionary<string, object?>
                {
                    ["visualType"] = "shape",
                    ["drillFilterOtherVisuals"] = true
                }
            };
        }

        private static object BuildVisualContainerFromSlicer(SlicerDefinition slicer, int index)
        {
            return new Dictionary<string, object?>
            {
                ["$schema"] = VisualContainerSchemaUrl,
                ["name"] = BuildSlicerVisualName(slicer),
                ["position"] = new Dictionary<string, object?>
                {
                    ["x"] = slicer.X,
                    ["y"] = slicer.Y,
                    ["z"] = 2000 + index,
                    ["height"] = slicer.Height,
                    ["width"] = slicer.Width,
                    ["tabOrder"] = index + 1
                },
                ["visual"] = new Dictionary<string, object?>
                {
                    ["visualType"] = "slicer",
                    ["query"] = new Dictionary<string, object?>
                    {
                        ["queryState"] = new Dictionary<string, object?>
                        {
                            ["Values"] = new Dictionary<string, object?>
                            {
                                ["projections"] = new[]
                                {
                                    new Dictionary<string, object?>
                                    {
                                        ["queryRef"] = slicer.Field,
                                        ["nativeQueryRef"] = slicer.Field,
                                        ["active"] = true
                                    }
                                }
                            }
                        }
                    },
                    ["objects"] = new Dictionary<string, object?>
                    {
                        ["data"] = new[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["properties"] = new Dictionary<string, object?>
                                {
                                    ["mode"] = new Dictionary<string, object?>
                                    {
                                        ["expr"] = new Dictionary<string, object?>
                                        {
                                            ["Literal"] = new Dictionary<string, object?>
                                            {
                                                ["Value"] = $"'{slicer.Type}'"
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    ["drillFilterOtherVisuals"] = true
                }
            };
        }

        private static string BuildSlicerVisualName(SlicerDefinition slicer)
        {
            var fieldPart = string.IsNullOrWhiteSpace(slicer.Field)
                ? "Slicer"
                : new string(slicer.Field.Where(char.IsLetterOrDigit).ToArray());

            return string.IsNullOrWhiteSpace(fieldPart)
                ? "Slicer"
                : $"Slicer{fieldPart}";
        }

        private static void ValidateReportDefinition(ReportDefinitionDocument document)
        {
            var pageIds = document.Pages
                .Select(ResolvePageId)
                .Where(pageId => !string.IsNullOrWhiteSpace(pageId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (pageIds.Count == 0)
            {
                throw new InvalidOperationException("Report definition must contain at least one page with a valid Name.");
            }

            var errors = new System.Collections.Generic.List<string>();

            if (string.IsNullOrWhiteSpace(document.Name))
            {
                errors.Add("Report name is required.");
            }

            var duplicatePageNames = document.Pages
                .Select(ResolvePageId)
                .Where(pageId => !string.IsNullOrWhiteSpace(pageId))
                .GroupBy(pageId => pageId, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            if (duplicatePageNames.Length > 0)
            {
                errors.Add("Duplicate page names found: " + string.Join(", ", duplicatePageNames) + ".");
            }

            var duplicateOrders = document.Pages
                .GroupBy(page => page.Order)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            if (duplicateOrders.Length > 0)
            {
                errors.Add("Duplicate page order values found: " + string.Join(", ", duplicateOrders) + ".");
            }

            var duplicateSanitizedPageFolders = document.Pages
                .Select(page => SanitizeDirectoryName(ResolvePageId(page)))
                .GroupBy(pageFolder => pageFolder, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            if (duplicateSanitizedPageFolders.Length > 0)
            {
                errors.Add("Duplicate sanitized page folder names found: " + string.Join(", ", duplicateSanitizedPageFolders) + ".");
            }

            foreach (var page in document.Pages)
            {
                if (string.IsNullOrWhiteSpace(page.Name))
                {
                    errors.Add("Page name is required.");
                }

                if (string.IsNullOrWhiteSpace(page.PageId))
                {
                    errors.Add($"Page '{page.Name}' must include PageId.");
                }

                if (string.IsNullOrWhiteSpace(page.DisplayName))
                {
                    errors.Add($"Page '{page.Name}' must have a display name.");
                }

                if (page.Order < 0)
                {
                    errors.Add($"Page '{page.Name}' has negative order '{page.Order}'.");
                }

                if (page.Canvas.Width <= 0 || page.Canvas.Height <= 0)
                {
                    errors.Add($"Page '{page.Name}' has invalid canvas size {page.Canvas.Width}x{page.Canvas.Height}.");
                }
            }

            foreach (var visual in document.VisualPositions)
            {
                var visualPageId = ResolveVisualPageId(visual);
                if (string.IsNullOrWhiteSpace(visualPageId) || !pageIds.Contains(visualPageId))
                {
                    errors.Add($"VisualPosition '{visual.VisualId}' references missing page '{visualPageId}'.");
                }

                if (string.IsNullOrWhiteSpace(visual.VisualId))
                {
                    errors.Add("VisualPosition must include a VisualId.");
                }

                if (visual.Width <= 0 || visual.Height <= 0)
                {
                    errors.Add($"VisualPosition '{visual.VisualId}' has invalid size {visual.Width}x{visual.Height}.");
                }
            }

            var duplicateVisualIds = document.VisualPositions
                .Where(visual => !string.IsNullOrWhiteSpace(visual.VisualId))
                .GroupBy(visual => visual.VisualId, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            if (duplicateVisualIds.Length > 0)
            {
                errors.Add("Duplicate visual IDs found: " + string.Join(", ", duplicateVisualIds) + ".");
            }

            foreach (var slicer in document.Slicers)
            {
                var slicerPageId = ResolveSlicerPageId(slicer);
                if (string.IsNullOrWhiteSpace(slicerPageId) || !pageIds.Contains(slicerPageId))
                {
                    errors.Add($"Slicer field '{slicer.Field}' references missing page '{slicerPageId}'.");
                }

                if (string.IsNullOrWhiteSpace(slicer.Field))
                {
                    errors.Add("Slicer field is required.");
                }

                if (string.IsNullOrWhiteSpace(slicer.Type))
                {
                    errors.Add($"Slicer field '{slicer.Field}' must specify a type.");
                }

                if (slicer.Width <= 0 || slicer.Height <= 0)
                {
                    errors.Add($"Slicer field '{slicer.Field}' has invalid size {slicer.Width}x{slicer.Height}.");
                }
            }

            foreach (var bookmark in document.Bookmarks)
            {
                var bookmarkPageId = ResolveBookmarkPageId(bookmark);
                if (string.IsNullOrWhiteSpace(bookmarkPageId) || !pageIds.Contains(bookmarkPageId))
                {
                    errors.Add($"Bookmark '{bookmark.Name}' references missing page '{bookmarkPageId}'.");
                }

                if (string.IsNullOrWhiteSpace(bookmark.BookmarkId))
                {
                    errors.Add($"Bookmark '{bookmark.Name}' must include BookmarkId.");
                }
            }

            var duplicateBookmarkIds = document.Bookmarks
                .Where(bookmark => !string.IsNullOrWhiteSpace(bookmark.BookmarkId))
                .GroupBy(bookmark => bookmark.BookmarkId, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            if (duplicateBookmarkIds.Length > 0)
            {
                errors.Add("Duplicate bookmark IDs found: " + string.Join(", ", duplicateBookmarkIds) + ".");
            }

            if (string.IsNullOrWhiteSpace(document.Navigation.DefaultPage) || !pageIds.Contains(document.Navigation.DefaultPage))
            {
                errors.Add($"Navigation.DefaultPage references missing page '{document.Navigation.DefaultPage}'.");
            }

            foreach (var menuPage in document.Navigation.Menu)
            {
                if (string.IsNullOrWhiteSpace(menuPage) || !pageIds.Contains(menuPage))
                {
                    errors.Add($"Navigation.Menu references missing page '{menuPage}'.");
                }
            }

            foreach (var theme in document.Themes)
            {
                if (string.IsNullOrWhiteSpace(theme.Path))
                {
                    errors.Add($"Theme '{theme.Name}' must define a file path.");
                }
            }

            if (errors.Count > 0)
            {
                var message = "Invalid report definition:" + Environment.NewLine
                    + string.Join(Environment.NewLine, errors.Select(error => " - " + error));

                throw new InvalidOperationException(message);
            }
        }

        private static string BuildNativeReportJson(ReportDefinitionDocument document)
        {
            var reportDocument = new
            {
                name = document.Name,
                version = ReportJsonVersion,
                pages = document.Pages
                    .OrderBy(page => page.Order)
                    .Select(page => new
                    {
                        name = ResolvePageId(page),
                        displayName = page.DisplayName,
                        order = page.Order
                    })
                    .ToArray(),
                navigation = new
                {
                    defaultPage = document.Navigation.DefaultPage,
                    menu = document.Navigation.Menu
                },
                bookmarks = document.Bookmarks
                    .Select(bookmark => new
                    {
                        name = ResolveBookmarkId(bookmark),
                        displayName = bookmark.Name,
                        page = ResolveBookmarkPageId(bookmark),
                        isDefault = bookmark.IsDefault
                    })
                    .ToArray()
            };

            return JsonSerializer.Serialize(
                reportDocument,
                JsonOptions);
        }

        private static void ValidateGeneratedOutput(string outputRootPath, ReportDefinitionDocument document)
        {
            var requiredRootFiles = new[]
            {
                Path.Combine(outputRootPath, "definition.pbir"),
                Path.Combine(outputRootPath, "report.json"),
                Path.Combine(outputRootPath, "reportExtensions.json")
            };

            foreach (var filePath in requiredRootFiles)
            {
                if (!File.Exists(filePath))
                {
                    throw new InvalidOperationException($"Generated PBIP output is missing required file '{filePath}'.");
                }

                ValidateJsonFile(filePath);
            }

            var pagesPath = Path.Combine(outputRootPath, "pages");
            foreach (var page in document.Pages)
            {
                var pageJsonPath = Path.Combine(pagesPath, SanitizeDirectoryName(ResolvePageId(page)), "page.json");
                if (!File.Exists(pageJsonPath))
                {
                    throw new InvalidOperationException($"Generated PBIP output is missing required page file '{pageJsonPath}'.");
                }

                ValidateJsonFile(pageJsonPath);
            }
        }

        private static void ValidateTemplateOutput(string outputRootPath)
        {
            var requiredFiles = new[]
            {
                Path.Combine(outputRootPath, "definition.pbir"),
                Path.Combine(outputRootPath, "definition", "report.json"),
                Path.Combine(outputRootPath, "definition", "pages", "pages.json")
            };

            foreach (var filePath in requiredFiles)
            {
                if (!File.Exists(filePath))
                {
                    throw new InvalidOperationException($"Generated PBIP output is missing required template file '{filePath}'.");
                }

                ValidateJsonFile(filePath);
            }
        }

        private static void ValidateJsonFile(string filePath)
        {
            var fileContents = File.ReadAllText(filePath);
            try
            {
                using var _ = JsonDocument.Parse(fileContents);
            }
            catch (JsonException jsonException)
            {
                throw new InvalidOperationException($"Generated JSON file '{filePath}' is invalid.", jsonException);
            }
        }

        private static string ResolvePageId(ReportPageDefinition page)
        {
            return string.IsNullOrWhiteSpace(page.PageId) ? page.Name : page.PageId;
        }

        private static string ResolveVisualPageId(VisualPositionDefinition visual)
        {
            return string.IsNullOrWhiteSpace(visual.PageId) ? visual.Page : visual.PageId;
        }

        private static string ResolveSlicerPageId(SlicerDefinition slicer)
        {
            return string.IsNullOrWhiteSpace(slicer.PageId) ? slicer.Page : slicer.PageId;
        }

        private static string ResolveBookmarkPageId(BookmarkDefinition bookmark)
        {
            return string.IsNullOrWhiteSpace(bookmark.PageId) ? bookmark.Page : bookmark.PageId;
        }

        private static string ResolveBookmarkId(BookmarkDefinition bookmark)
        {
            return string.IsNullOrWhiteSpace(bookmark.BookmarkId) ? bookmark.Name : bookmark.BookmarkId;
        }

        private static string BuildReportExtensionsJson()
        {
            var extensionsDocument = new
            {
                entities = Array.Empty<object>()
            };

            return JsonSerializer.Serialize(
                extensionsDocument,
                JsonOptions);
        }

        private static string? ResolveThemePath(string configuredPath, string reportRootPath, string? themeSourceRootPath)
        {
            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            var reportRelativeCandidate = Path.GetFullPath(Path.Combine(reportRootPath, configuredPath));
            if (File.Exists(reportRelativeCandidate))
            {
                return reportRelativeCandidate;
            }

            if (!string.IsNullOrWhiteSpace(themeSourceRootPath))
            {
                var combinedCandidate = Path.GetFullPath(Path.Combine(themeSourceRootPath, configuredPath));
                if (File.Exists(combinedCandidate))
                {
                    return combinedCandidate;
                }

                var fileNameOnlyCandidate = Path.Combine(themeSourceRootPath, Path.GetFileName(configuredPath));
                if (File.Exists(fileNameOnlyCandidate))
                {
                    return fileNameOnlyCandidate;
                }

                var themeFolderCandidate = Path.Combine(themeSourceRootPath, "theme", Path.GetFileName(configuredPath));
                if (File.Exists(themeFolderCandidate))
                {
                    return themeFolderCandidate;
                }
            }

            return null;
        }

        private static void CopyAdditionalStaticResources(string registeredResourcesPath, string? themeSourceRootPath)
        {
            if (string.IsNullOrWhiteSpace(themeSourceRootPath))
            {
                return;
            }

            var candidateDirectories = new[]
            {
                Path.Combine(themeSourceRootPath, "staticResources", "RegisteredResources"),
                Path.Combine(themeSourceRootPath, "RegisteredResources")
            };

            foreach (var candidateDirectory in candidateDirectories)
            {
                if (!Directory.Exists(candidateDirectory))
                {
                    continue;
                }

                foreach (var sourceFilePath in Directory.GetFiles(candidateDirectory))
                {
                    var destinationPath = Path.Combine(registeredResourcesPath, Path.GetFileName(sourceFilePath));
                    if (!File.Exists(destinationPath))
                    {
                        File.Copy(sourceFilePath, destinationPath, overwrite: false);
                    }
                }
            }
        }

        private static string SanitizeDirectoryName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = new string(value.Trim().ToCharArray());
            foreach (var invalidChar in invalidChars)
            {
                cleaned = cleaned.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(cleaned) ? "Page" : cleaned;
        }

        private static void SafeDeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of temporary directories.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of temporary directories.
            }
        }
    }
}

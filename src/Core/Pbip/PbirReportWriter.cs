using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    public sealed class PbirReportWriter
    {
        private const string DefinitionPbirVersion = "4.0";
        private const string ReportJsonVersion = "1.0";
        private const string PageSchemaUrl = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/page/2.1.0/schema.json";
        private const string VisualContainerSchemaUrl = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/visualContainer/2.10.0/schema.json";
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public void WriteFromJson(string reportDefinitionJson, string reportRootPath, string semanticModelRelativePath, string? themeSourceRootPath = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reportDefinitionJson);
            var document = JsonSerializer.Deserialize<ReportDefinitionDocument>(reportDefinitionJson, JsonOptions) ?? throw new InvalidOperationException("Unable to deserialize report definition JSON.");
            Write(document, reportRootPath, semanticModelRelativePath, themeSourceRootPath);
        }

        public void Write(ReportDefinitionDocument document, string reportRootPath, string semanticModelRelativePath, string? themeSourceRootPath = null)
        {
            ArgumentNullException.ThrowIfNull(document); ArgumentException.ThrowIfNullOrWhiteSpace(reportRootPath); ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRelativePath);
            ValidateReportDefinition(document);
            var parent = Path.GetDirectoryName(reportRootPath) ?? throw new InvalidOperationException($"Unable to determine parent directory for '{reportRootPath}'.");
            Directory.CreateDirectory(parent);
            var staging = Path.Combine(parent, $"{Path.GetFileName(reportRootPath)}.tmp-{Guid.NewGuid():N}");
            try { WriteToDirectory(document, staging, semanticModelRelativePath, themeSourceRootPath); ReplaceDirectoryAtomically(staging, reportRootPath); }
            finally { SafeDeleteDirectory(staging); }
        }

        public void WriteFromTemplate(string templateReportRootPath, string reportRootPath, string semanticModelRelativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(templateReportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(reportRootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticModelRelativePath);
            if (!Directory.Exists(templateReportRootPath)) throw new InvalidOperationException($"Report template directory '{templateReportRootPath}' does not exist.");

            var parent = Path.GetDirectoryName(reportRootPath) ?? throw new InvalidOperationException($"Unable to determine parent directory for '{reportRootPath}'.");
            Directory.CreateDirectory(parent);
            var staging = Path.Combine(parent, $"{Path.GetFileName(reportRootPath)}.tmp-{Guid.NewGuid():N}");

            try
            {
                // The real template is the authoritative report structure. Copy it first and
                // deliberately do not reconstruct pages, visuals, resources, or extensions.
                CopyDirectoryRecursively(templateReportRootPath, staging);
                Console.WriteLine($"Established generated PBIP from authoritative report template '{templateReportRootPath}'.");

                // The dataset pointer and registered-image metadata are the only report-level fields intentionally patched.
                var definitionPbirPath = Path.Combine(staging, "definition.pbir");
                if (!File.Exists(definitionPbirPath))
                    throw new InvalidOperationException($"Template is missing required file '{definitionPbirPath}'.");
                File.WriteAllText(definitionPbirPath, BuildDefinitionPbir(semanticModelRelativePath), new System.Text.UTF8Encoding(false));

                PbirReportResourcePackagePatcher.RegisterTemplatePngs(staging);
                ValidateTemplateOutput(staging);
                ReplaceDirectoryAtomically(staging, reportRootPath);
            }
            finally { SafeDeleteDirectory(staging); }
        }

        private static void WriteToDirectory(ReportDefinitionDocument document, string root, string semanticModelRelativePath, string? themeSourceRootPath)
        {
            Directory.CreateDirectory(root);
            var pages = Path.Combine(root, "pages");
            var registered = Path.Combine(root, "staticResources", "RegisteredResources");
            Directory.CreateDirectory(pages); Directory.CreateDirectory(registered);
            File.WriteAllText(Path.Combine(root, "definition.pbir"), BuildDefinitionPbir(semanticModelRelativePath));
            File.WriteAllText(Path.Combine(root, "report.json"), BuildNativeReportJson(document));
            File.WriteAllText(Path.Combine(root, "reportExtensions.json"), BuildReportExtensionsJson());
            foreach (var page in document.Pages)
            {
                var folder = Path.Combine(pages, SanitizeDirectoryName(ResolvePageId(page)));
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "page.json"), BuildPageJson(document, page));
            }
            foreach (var theme in document.Themes)
            {
                var source = ResolveThemePath(theme.Path, root, themeSourceRootPath);
                if (source is null || !File.Exists(source)) throw new InvalidOperationException($"Theme '{theme.Name}' references missing file '{theme.Path}'.");
                File.Copy(source, Path.Combine(registered, Path.GetFileName(source)), true);
            }
            CopyAdditionalStaticResources(registered, themeSourceRootPath);
            RegisterStaticResourceImages(root);
            ValidateGeneratedOutput(root, document);
        }

        private static void RegisterStaticResourceImages(string reportRoot)
        {
            var reportJsonPath = ResolveReportJsonPath(reportRoot);
            var registeredRoot = ResolveRegisteredResourcesPath(reportRoot);
            if (!File.Exists(reportJsonPath) || !Directory.Exists(registeredRoot)) return;

            var report = JsonNode.Parse(File.ReadAllText(reportJsonPath)) as JsonObject
                ?? throw new InvalidOperationException($"Report definition '{reportJsonPath}' must contain a JSON object.");
            var packages = report["resourcePackages"] as JsonArray;
            if (packages is null)
            {
                packages = new JsonArray();
                report["resourcePackages"] = packages;
            }

            JsonObject? registeredPackage = packages
                .OfType<JsonObject>()
                .FirstOrDefault(p => string.Equals(p["name"]?.GetValue<string>(), "RegisteredResources", StringComparison.OrdinalIgnoreCase)
                                  && string.Equals(p["type"]?.GetValue<string>(), "RegisteredResources", StringComparison.OrdinalIgnoreCase));
            if (registeredPackage is null)
            {
                registeredPackage = new JsonObject
                {
                    ["name"] = "RegisteredResources",
                    ["type"] = "RegisteredResources",
                    ["items"] = new JsonArray()
                };
                packages.Add(registeredPackage);
            }

            var items = registeredPackage["items"] as JsonArray ?? new JsonArray();
            registeredPackage["items"] = items;
            var registeredNames = items.OfType<JsonObject>()
                .Select(i => i["name"]?.GetValue<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp" };
            var imageFiles = Directory.GetFiles(registeredRoot, "*", SearchOption.AllDirectories)
                .Where(f => imageExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

            foreach (var imageFile in imageFiles)
            {
                var itemName = Path.GetRelativePath(registeredRoot, imageFile).Replace('\\', '/');
                if (!registeredNames.Add(itemName)) continue;
                items.Add(new JsonObject
                {
                    ["name"] = itemName,
                    ["path"] = itemName,
                    ["type"] = "Image"
                });
                Console.WriteLine($"Registered StaticResources image: {itemName}");
            }

            File.WriteAllText(reportJsonPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new System.Text.UTF8Encoding(false));
        }

        private static string ResolveReportJsonPath(string reportRoot)
        {
            var legacy = Path.Combine(reportRoot, "report.json");
            if (File.Exists(legacy)) return legacy;
            var pbir = Path.Combine(reportRoot, "definition", "report.json");
            if (File.Exists(pbir)) return pbir;
            throw new InvalidOperationException($"Unable to locate report.json under '{reportRoot}'.");
        }

        private static string ResolveRegisteredResourcesPath(string reportRoot)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(reportRoot, "StaticResources", "RegisteredResources"),
                Path.Combine(reportRoot, "staticResources", "RegisteredResources")
            })
            {
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new InvalidOperationException($"Unable to locate StaticResources/RegisteredResources under '{reportRoot}'.");
        }

        private static void ReplaceDirectoryAtomically(string staging, string target)
        {
            var backup = $"{target}.bak-{Guid.NewGuid():N}"; var exists = Directory.Exists(target); if (exists) Directory.Move(target, backup);
            try { Directory.Move(staging, target); if (exists) SafeDeleteDirectory(backup); }
            catch { if (Directory.Exists(target)) SafeDeleteDirectory(target); if (exists && Directory.Exists(backup)) Directory.Move(backup, target); throw; }
        }

        private static void CopyDirectoryRecursively(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) { var dest = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, true); }
        }

        private static string BuildDefinitionPbir(string path) => JsonSerializer.Serialize(new { version = DefinitionPbirVersion, datasetReference = new { byPath = new { path = path.Replace("\\", "/", StringComparison.Ordinal) } } }, JsonOptions);
        private static string BuildReportExtensionsJson() => JsonSerializer.Serialize(new { entities = Array.Empty<object>() }, JsonOptions);

        private static string BuildPageJson(ReportDefinitionDocument document, ReportPageDefinition page)
        {
            var pageId = ResolvePageId(page);
            var containers = document.VisualPositions.Where(v => string.Equals(ResolveVisualPageId(v), pageId, StringComparison.OrdinalIgnoreCase)).Select((v, i) => (object)BuildVisualContainerFromPosition(v, i)).ToList();
            containers.AddRange(document.Slicers.Where(s => string.Equals(ResolveSlicerPageId(s), pageId, StringComparison.OrdinalIgnoreCase)).Select((s, i) => (object)BuildVisualContainerFromSlicer(s, i + containers.Count)));
            return JsonSerializer.Serialize(new Dictionary<string, object?> { ["$schema"] = PageSchemaUrl, ["name"] = pageId, ["displayName"] = page.DisplayName, ["displayOption"] = "FitToPage", ["height"] = page.Canvas.Height, ["width"] = page.Canvas.Width, ["visualContainers"] = containers }, JsonOptions);
        }

        private static object BuildVisualContainerFromPosition(VisualPositionDefinition v, int i)
        {
            if (v.VisualContainer is not null) return v.VisualContainer;
            return new Dictionary<string, object?> { ["$schema"] = VisualContainerSchemaUrl, ["name"] = v.VisualId, ["position"] = new Dictionary<string, object?> { ["x"] = v.X, ["y"] = v.Y, ["z"] = 1000 + i, ["height"] = v.Height, ["width"] = v.Width, ["tabOrder"] = i + 1 }, ["visual"] = new Dictionary<string, object?> { ["visualType"] = "shape", ["drillFilterOtherVisuals"] = true } };
        }

        private static object BuildVisualContainerFromSlicer(SlicerDefinition s, int i) => new Dictionary<string, object?> { ["$schema"] = VisualContainerSchemaUrl, ["name"] = BuildSlicerVisualName(s), ["position"] = new Dictionary<string, object?> { ["x"] = s.X, ["y"] = s.Y, ["z"] = 2000 + i, ["height"] = s.Height, ["width"] = s.Width, ["tabOrder"] = i + 1 }, ["visual"] = new Dictionary<string, object?> { ["visualType"] = "slicer", ["query"] = new Dictionary<string, object?> { ["queryState"] = new Dictionary<string, object?> { ["Values"] = new Dictionary<string, object?> { ["projections"] = new[] { new Dictionary<string, object?> { ["queryRef"] = s.Field, ["nativeQueryRef"] = s.Field, ["active"] = true } } } } }, ["objects"] = new Dictionary<string, object?> { ["data"] = new[] { new Dictionary<string, object?> { ["properties"] = new Dictionary<string, object?> { ["mode"] = new Dictionary<string, object?> { ["expr"] = new Dictionary<string, object?> { ["Literal"] = new Dictionary<string, object?> { ["Value"] = $"'{s.Type}'" } } } } } } }, ["drillFilterOtherVisuals"] = true } };
        private static string BuildSlicerVisualName(SlicerDefinition s) { var p = string.IsNullOrWhiteSpace(s.Field) ? "Slicer" : new string(s.Field.Where(char.IsLetterOrDigit).ToArray()); return string.IsNullOrWhiteSpace(p) ? "Slicer" : $"Slicer{p}"; }

        private static string BuildNativeReportJson(ReportDefinitionDocument d) => JsonSerializer.Serialize(new { name = d.Name, version = ReportJsonVersion, pages = d.Pages.OrderBy(p => p.Order).Select(p => new { name = ResolvePageId(p), displayName = p.DisplayName, order = p.Order }).ToArray(), navigation = new { defaultPage = d.Navigation.DefaultPage, menu = d.Navigation.Menu }, bookmarks = d.Bookmarks.Select(b => new { name = ResolveBookmarkId(b), displayName = b.Name, page = ResolveBookmarkPageId(b), isDefault = b.IsDefault }).ToArray() }, JsonOptions);

        private static void ValidateReportDefinition(ReportDefinitionDocument d)
        {
            var ids = d.Pages.Select(ResolvePageId).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase); if (ids.Count == 0) throw new InvalidOperationException("Report definition must contain at least one page with a valid Name.");
            var errors = new List<string>(); if (string.IsNullOrWhiteSpace(d.Name)) errors.Add("Report name is required.");
            if (d.Pages.Select(ResolvePageId).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) errors.Add("Duplicate page names found.");
            if (d.Pages.GroupBy(p => p.Order).Any(g => g.Count() > 1)) errors.Add("Duplicate page order values found.");
            foreach (var p in d.Pages) { if (string.IsNullOrWhiteSpace(p.Name)) errors.Add("Page name is required."); if (string.IsNullOrWhiteSpace(p.PageId)) errors.Add($"Page '{p.Name}' must include PageId."); if (string.IsNullOrWhiteSpace(p.DisplayName)) errors.Add($"Page '{p.Name}' must have a display name."); if (p.Order < 0) errors.Add($"Page '{p.Name}' has negative order '{p.Order}'."); if (p.Canvas.Width <= 0 || p.Canvas.Height <= 0) errors.Add($"Page '{p.Name}' has invalid canvas size."); }
            foreach (var v in d.VisualPositions) { var pid = ResolveVisualPageId(v); if (string.IsNullOrWhiteSpace(pid) || !ids.Contains(pid)) errors.Add($"VisualPosition '{v.VisualId}' references missing page '{pid}'."); if (string.IsNullOrWhiteSpace(v.VisualId)) errors.Add("VisualPosition must include a VisualId."); if (v.Width <= 0 || v.Height <= 0) errors.Add($"VisualPosition '{v.VisualId}' has invalid size."); }
            if (d.VisualPositions.Where(v => !string.IsNullOrWhiteSpace(v.VisualId)).GroupBy(v => v.VisualId, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) errors.Add("Duplicate visual IDs found.");
            foreach (var s in d.Slicers) { var pid = ResolveSlicerPageId(s); if (string.IsNullOrWhiteSpace(pid) || !ids.Contains(pid)) errors.Add($"Slicer field '{s.Field}' references missing page '{pid}'."); if (string.IsNullOrWhiteSpace(s.Field)) errors.Add("Slicer field is required."); if (string.IsNullOrWhiteSpace(s.Type)) errors.Add($"Slicer field '{s.Field}' must specify a type."); }
            foreach (var b in d.Bookmarks) { var pid = ResolveBookmarkPageId(b); if (string.IsNullOrWhiteSpace(pid) || !ids.Contains(pid)) errors.Add($"Bookmark '{b.Name}' references missing page '{pid}'."); if (string.IsNullOrWhiteSpace(b.BookmarkId)) errors.Add($"Bookmark '{b.Name}' must include BookmarkId."); }
            if (string.IsNullOrWhiteSpace(d.Navigation.DefaultPage) || !ids.Contains(d.Navigation.DefaultPage)) errors.Add($"Navigation.DefaultPage references missing page '{d.Navigation.DefaultPage}'.");
            foreach (var m in d.Navigation.Menu) if (string.IsNullOrWhiteSpace(m) || !ids.Contains(m)) errors.Add($"Navigation.Menu references missing page '{m}'.");
            if (errors.Count > 0) throw new InvalidOperationException("Invalid report definition:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => " - " + e)));
        }

        private static void ValidateGeneratedOutput(string root, ReportDefinitionDocument d)
        {
            foreach (var f in new[] { Path.Combine(root, "definition.pbir"), Path.Combine(root, "report.json"), Path.Combine(root, "reportExtensions.json") }) { if (!File.Exists(f)) throw new InvalidOperationException($"Generated PBIP output is missing required file '{f}'."); ValidateJsonFile(f); }
            foreach (var p in d.Pages) { var f = Path.Combine(root, "pages", SanitizeDirectoryName(ResolvePageId(p)), "page.json"); if (!File.Exists(f)) throw new InvalidOperationException($"Generated PBIP output is missing required page file '{f}'."); ValidateJsonFile(f); }
        }
        private static void ValidateTemplateOutput(string root)
        {
            foreach (var f in new[] { Path.Combine(root, "definition.pbir"), Path.Combine(root, "definition", "report.json"), Path.Combine(root, "definition", "reportExtensions.json"), Path.Combine(root, "definition", "pages", "pages.json") })
            {
                if (!File.Exists(f)) throw new InvalidOperationException($"Generated PBIP output is missing required template file '{f}'.");
                ValidateJsonFile(f);
            }
        }
        private static void ValidateJsonFile(string file) { try { using var _ = JsonDocument.Parse(File.ReadAllText(file)); } catch (JsonException e) { throw new InvalidOperationException($"Generated JSON file '{file}' is invalid.", e); } }
        private static string ResolvePageId(ReportPageDefinition p) => string.IsNullOrWhiteSpace(p.PageId) ? p.Name : p.PageId;
        private static string ResolveVisualPageId(VisualPositionDefinition v) => string.IsNullOrWhiteSpace(v.PageId) ? v.Page : v.PageId;
        private static string ResolveSlicerPageId(SlicerDefinition s) => string.IsNullOrWhiteSpace(s.PageId) ? s.Page : s.PageId;
        private static string ResolveBookmarkPageId(BookmarkDefinition b) => string.IsNullOrWhiteSpace(b.PageId) ? b.Page : b.PageId;
        private static string ResolveBookmarkId(BookmarkDefinition b) => string.IsNullOrWhiteSpace(b.BookmarkId) ? b.Name : b.BookmarkId;
        private static string? ResolveThemePath(string configuredPath, string root, string? themeRoot) { if (Path.IsPathRooted(configuredPath)) return configuredPath; var a = Path.GetFullPath(Path.Combine(root, configuredPath)); if (File.Exists(a)) return a; if (!string.IsNullOrWhiteSpace(themeRoot)) { var b = Path.GetFullPath(Path.Combine(themeRoot, configuredPath)); if (File.Exists(b)) return b; var c = Path.Combine(themeRoot, Path.GetFileName(configuredPath)); if (File.Exists(c)) return c; var d = Path.Combine(themeRoot, "theme", Path.GetFileName(configuredPath)); if (File.Exists(d)) return d; } return null; }
        private static void CopyAdditionalStaticResources(registered, string? themeRoot) { if (string.IsNullOrWhiteSpace(themeRoot)) return; foreach (var dir in new[] { Path.Combine(themeRoot, "staticResources", "RegisteredResources"), Path.Combine(themeRoot, "RegisteredResources") }) if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir)) { var d = Path.Combine(registered, Path.GetFileName(f)); if (!File.Exists(d)) File.Copy(f, d); } }
        private static string SanitizeDirectoryName(string value) { var s = value.Trim(); foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return string.IsNullOrWhiteSpace(s) ? "Page" : s; }
        private static void SafeDeleteDirectory(string path) { if (!Directory.Exists(path)) return; try { Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
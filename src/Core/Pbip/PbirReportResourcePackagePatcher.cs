using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    /// <summary>
    /// Applies the minimal resource-package metadata patch required by the
    /// authoritative PBIR report template. The report itself is never rebuilt.
    /// </summary>
    internal static class PbirReportResourcePackagePatcher
    {
        private static readonly HashSet<string> ExpectedPngNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Banner7758505367451983.png",
            "LOGO23969025434519886.png"
        };

        public static void RegisterTemplatePngs(string reportRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reportRoot);

            var reportJsonPath = Path.Combine(reportRoot, "definition", "report.json");
            if (!File.Exists(reportJsonPath))
                throw new InvalidOperationException($"Template report metadata was not found: '{reportJsonPath}'.");

            var registeredRoot = ResolveRegisteredResourcesPath(reportRoot);
            var pngFiles = Directory.GetFiles(registeredRoot, "*.png", SearchOption.AllDirectories)
                .Where(path => ExpectedPngNames.Contains(Path.GetFileName(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var missingFiles = ExpectedPngNames
                .Except(pngFiles.Select(path => Path.GetFileName(path)), StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (missingFiles.Length > 0)
                throw new InvalidOperationException(
                    "Template report is missing expected PNG resources: " + string.Join(", ", missingFiles));

            var report = JsonNode.Parse(File.ReadAllText(reportJsonPath)) as JsonObject
                ?? throw new InvalidOperationException($"Report metadata '{reportJsonPath}' must contain a JSON object.");

            var packages = report["resourcePackages"] as JsonArray;
            if (packages is null)
            {
                packages = new JsonArray();
                report["resourcePackages"] = packages;
            }

            var package = packages
                .OfType<JsonObject>()
                .FirstOrDefault(item =>
                    string.Equals(item["name"]?.GetValue<string>(), "RegisteredResources", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item["type"]?.GetValue<string>(), "RegisteredResources", StringComparison.OrdinalIgnoreCase));

            if (package is null)
            {
                package = new JsonObject
                {
                    ["name"] = "RegisteredResources",
                    ["type"] = "RegisteredResources",
                    ["items"] = new JsonArray()
                };
                packages.Add(package);
            }

            var items = package["items"] as JsonArray ?? new JsonArray();
            package["items"] = items;

            var registeredNames = items
                .OfType<JsonObject>()
                .Select(item => item["name"]?.GetValue<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var pngFile in pngFiles)
            {
                var itemName = Path.GetRelativePath(registeredRoot, pngFile).Replace('\\', '/');
                if (!registeredNames.Add(itemName))
                    continue;

                items.Add(new JsonObject
                {
                    ["name"] = itemName,
                    ["path"] = itemName,
                    ["type"] = "Image"
                });

                Console.WriteLine($"PBIR-RESOURCE-PATCH|Registered PNG|{itemName}");
            }

            File.WriteAllText(
                reportJsonPath,
                report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));

            ValidateRegistrations(reportJsonPath);
        }

        private static string ResolveRegisteredResourcesPath(string reportRoot)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(reportRoot, "StaticResources", "RegisteredResources"),
                Path.Combine(reportRoot, "staticResources", "RegisteredResources")
            })
            {
                if (Directory.Exists(candidate))
                    return candidate;
            }

            throw new InvalidOperationException(
                $"Template report is missing StaticResources/RegisteredResources under '{reportRoot}'.");
        }

        private static void ValidateRegistrations(string reportJsonPath)
        {
            var report = JsonNode.Parse(File.ReadAllText(reportJsonPath)) as JsonObject
                ?? throw new InvalidOperationException($"Patched report metadata '{reportJsonPath}' is invalid JSON.");

            var packages = report["resourcePackages"] as JsonArray;
            var package = packages?
                .OfType<JsonObject>()
                .FirstOrDefault(item =>
                    string.Equals(item["name"]?.GetValue<string>(), "RegisteredResources", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item["type"]?.GetValue<string>(), "RegisteredResources", StringComparison.OrdinalIgnoreCase));
            var items = package?["items"] as JsonArray;

            var registrations = items?
                .OfType<JsonObject>()
                .Where(item => string.Equals(item["type"]?.GetValue<string>(), "Image", StringComparison.OrdinalIgnoreCase))
                .Select(item => item["name"]?.GetValue<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList() ?? new List<string?>();

            foreach (var expected in ExpectedPngNames)
            {
                var count = registrations.Count(name => string.Equals(name, expected, StringComparison.OrdinalIgnoreCase));
                if (count != 1)
                    throw new InvalidOperationException(
                        $"PBIR resource registration validation failed for '{expected}': expected exactly one Image registration, found {count}.");
            }

            Console.WriteLine($"PBIR-RESOURCE-PATCH|Validated|ExpectedPngCount={ExpectedPngNames.Count}");
        }
    }
}

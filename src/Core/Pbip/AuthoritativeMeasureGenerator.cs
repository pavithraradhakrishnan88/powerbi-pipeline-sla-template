using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerBiPipelineSlaTemplate.Core.Pbip;

internal static class AuthoritativeMeasureGenerator
{
    public static void Write(string repositoryRootPath, string measuresTablePath)
    {
        var source = Path.Combine(repositoryRootPath, "model", "tables", "_Measure Table", "measures");
        var definitions = new[]
        {
            ("M013", "Total Runs.json", 130),
            ("M014", "SLA Compliance %.json", 140),
            ("M015", "Breached Count.json", 150)
        };

        var text = File.ReadAllText(measuresTablePath);
        foreach (var (id, file, order) in definitions)
        {
            var path = Path.Combine(source, file);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var name = root.GetProperty("name").GetString()!;
            var expression = root.GetProperty("expression");
            var dax = expression.ValueKind == JsonValueKind.Array
                ? string.Join("", expression.EnumerateArray().Select(x => x.GetString() ?? ""))
                : expression.GetString() ?? "";
            var folder = root.TryGetProperty("displayFolder", out var f) ? f.GetString() : null;
            var format = root.TryGetProperty("formatString", out var fmt) ? fmt.GetString() : null;
            var block = $"\tmeasure '{name.Replace("'", "''")}' = {Regex.Replace(dax.Trim(), @\"\\s*\\r?\\n\\s*\", \" \")}\n"
                + (string.IsNullOrWhiteSpace(folder) ? "" : $"\t\tdisplayFolder: '{folder!.Replace("'", "''")}'\n")
                + (string.IsNullOrWhiteSpace(format) ? "" : $"\t\tformatString: '{format!.Replace("'", "''")}'\n") + "\n";
            text += block;
        }

        File.WriteAllText(measuresTablePath, text);
        var count = Regex.Matches(text, @"^\s*measure\s+'", RegexOptions.Multiline).Count;
        if (count != 23) throw new InvalidDataException($"Expected 23 generated measures, found {count}.");
        Console.WriteLine("Authoritative measure generation: 23 measures (M001-M023), including Total Runs, SLA Compliance %, and Breached Count.");
    }
}

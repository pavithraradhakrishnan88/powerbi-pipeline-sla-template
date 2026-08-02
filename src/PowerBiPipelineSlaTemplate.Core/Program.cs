using System;
using System.IO;
using PowerBiPipelineSlaTemplate.Core;
using PowerBiPipelineSlaTemplate.Core.Models;
using PowerBiPipelineSlaTemplate.Core.Serialization;

var outputPath = "metadata/metadata.json";
var dataDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data"));

if (args.Length >= 2 && string.Equals(args[0], "--extract-metadata", StringComparison.OrdinalIgnoreCase))
{
    for (var index = 1; index < args.Length; index++)
    {
        if (string.Equals(args[index], "--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
        {
            outputPath = args[index + 1];
            break;
        }
    }
}

var schemaReader = new SchemaReader(dataDirectory, message => Console.WriteLine(message));
var schema = schemaReader.Read();
var builder = new ModelBuilder();
var model = builder.Build(schema);
var json = builder.ToJson(model, true);

var absoluteOutputPath = Path.GetFullPath(outputPath);
var outputDirectory = Path.GetDirectoryName(absoluteOutputPath);
if (!string.IsNullOrWhiteSpace(outputDirectory))
{
    Directory.CreateDirectory(outputDirectory);
}

File.WriteAllText(absoluteOutputPath, json);
Console.WriteLine($"Wrote metadata to {absoluteOutputPath}");

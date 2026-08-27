using System;
using System.IO;
using System.Text.RegularExpressions;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using PowerBiPipelineSlaTemplate.Tests.Shared.Utilities;

namespace PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;

public sealed class PipelineTestFixture : IDisposable
{
    private readonly TemporaryWorkspace _workspace;

    public PipelineTestFixture(string scenarioName)
    {
        _workspace = new TemporaryWorkspace();

        DataDirectoryPath = _workspace.CreateDirectory("data");
        SampleCsvGenerator.CopyScenarioToWorkspace(scenarioName, DataDirectoryPath);

        var pbipRoot = _workspace.CreateDirectory("pbip");
        var repoRoot = WorkspacePaths.FindRepoRoot();

        var sourceMeasureDefinitionsPath = Path.Combine(repoRoot, "scripts", "metadata", "MeasureDefinitions.json");
        var destinationMeasureDefinitionsPath = _workspace.GetPath(Path.Combine("scripts", "metadata", "MeasureDefinitions.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(destinationMeasureDefinitionsPath)!);
        File.Copy(sourceMeasureDefinitionsPath, destinationMeasureDefinitionsPath, overwrite: true);

        var sourceReportTemplatePath = Path.Combine(repoRoot, "pbip", "Pipeline_SLA_Tracker.Report");
        ReportTemplateRootPath = Path.Combine(pbipRoot, "Pipeline_SLA_Tracker.Report");
        CopyDirectory(sourceReportTemplatePath, ReportTemplateRootPath);

        var sourceSemanticModelTemplatePath = Path.Combine(repoRoot, "pbip", "Pipeline_SLA_Tracker.SemanticModel");
        SemanticModelTemplateRootPath = Path.Combine(pbipRoot, "Pipeline_SLA_Tracker.SemanticModel");
        CopyDirectory(sourceSemanticModelTemplatePath, SemanticModelTemplateRootPath);

        // Fixture-boundary diagnostics: capture the exact relationships.tmdl that will be
        // supplied to the orchestrator before PatchDateVariations() consumes it.
        EmitScheduledStartRelationshipDiagnostics(
            Path.Combine(SemanticModelTemplateRootPath, "definition", "relationships.tmdl"));

        SemanticModelRootPath = Path.Combine(pbipRoot, "Pipeline SLA.SemanticModel");
        ReportRootPath = Path.Combine(pbipRoot, "Pipeline SLA.Report");
        MetadataOutputPath = _workspace.GetPath(Path.Combine("metadata", "metadata.json"));

        Options = new PipelineOptions
        {
            DataDirectoryPath = DataDirectoryPath,
            SemanticModelTemplateRootPath = SemanticModelTemplateRootPath,
            SemanticModelRootPath = SemanticModelRootPath,
            ReportRootPath = ReportRootPath,
            ReportTemplateRootPath = ReportTemplateRootPath,
            SemanticModelRelativePath = "../Pipeline SLA.SemanticModel",
            MetadataOutputPath = MetadataOutputPath,
            ThrowOnValidationError = true
        };
    }

    public PipelineOptions Options { get; }
    public string DataDirectoryPath { get; }
    public string SemanticModelTemplateRootPath { get; }
    public string SemanticModelRootPath { get; }
    public string ReportRootPath { get; }
    public string ReportTemplateRootPath { get; }
    public string MetadataOutputPath { get; }

    public void Dispose() => _workspace.Dispose();

    private static void EmitScheduledStartRelationshipDiagnostics(string relationshipsPath)
    {
        const string relationshipId = "db2083da-0a18-4172-ab57-a096ce539554";
        const string localDateTable = "LocalDateTable_9043e032-67a4-45e7-bf88-28fec57966b9";
        const string columnName = "ScheduledStart";
        const string factTable = "Fact_Pipeline_SampleData";

        Console.WriteLine($"TMDL-RELATIONSHIP-DIAGNOSTIC|Path={relationshipsPath}");
        if (!File.Exists(relationshipsPath))
        {
            Console.WriteLine("TMDL-RELATIONSHIP-DIAGNOSTIC|Exists=False|SizeBytes=0");
            return;
        }

        var relationships = File.ReadAllText(relationshipsPath);
        var newlineType = relationships.Contains("\r\n", StringComparison.Ordinal)
            ? "CRLF"
            : relationships.Contains("\n", StringComparison.Ordinal) ? "LF" : "NONE";
        Console.WriteLine($"TMDL-RELATIONSHIP-DIAGNOSTIC|Exists=True|SizeBytes={new FileInfo(relationshipsPath).Length}|Newline={newlineType}");

        var relationshipStart = relationships.IndexOf($"relationship {relationshipId}", StringComparison.Ordinal);
        var relationshipEnd = relationshipStart >= 0
            ? relationships.IndexOf("relationship ", relationshipStart + 1, StringComparison.Ordinal)
            : -1;
        if (relationshipStart >= 0)
        {
            if (relationshipEnd < 0) relationshipEnd = relationships.Length;
            var block = relationships[relationshipStart..relationshipEnd].TrimEnd('\r', '\n');
            Console.WriteLine("TMDL-RELATIONSHIP-DIAGNOSTIC|ScheduledStartBlock-BEGIN");
            Console.WriteLine(block);
            Console.WriteLine("TMDL-RELATIONSHIP-DIAGNOSTIC|ScheduledStartBlock-END");
        }
        else
        {
            Console.WriteLine("TMDL-RELATIONSHIP-DIAGNOSTIC|ScheduledStartBlock=NOT_FOUND");
        }

        // Mirror the known-good PatchDateVariations() matcher exactly so the diagnostic
        // reports the same expression being evaluated by production code.
        var relationshipPattern = $"relationship\\s+{Regex.Escape(relationshipId)}\\s+\\r?\\n" +
            "\\s*joinOnDateBehavior:\\s*datePartOnly\\s+\\r?\\n" +
            "\\s*fromColumn:\\s*" + Regex.Escape(factTable) + "\\." + Regex.Escape(columnName) + "\\s+\\r?\\n" +
            "\\s*toColumn:\\s*" + Regex.Escape(localDateTable) + "\\.Date";
        Console.WriteLine($"TMDL-RELATIONSHIP-DIAGNOSTIC|RegexPattern={relationshipPattern}");
        Console.WriteLine($"TMDL-RELATIONSHIP-DIAGNOSTIC|RegexIsMatch={Regex.IsMatch(relationships, relationshipPattern, RegexOptions.CultureInvariant)}");
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(sourcePath)) throw new DirectoryNotFoundException($"Source directory was not found: {sourcePath}");
        Directory.CreateDirectory(destinationPath);
        foreach (var sourceFile in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
    }
}

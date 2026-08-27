using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class PbipProjectWriterTests
{
    [Fact]
    public void Run_ShouldSucceed_ForNormalScenario()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var orchestrator = new PipelineOrchestrator();

        EmitScheduledStartRelationshipDiagnostics(fixture.SemanticModelRootPath);
        var result = orchestrator.Run(fixture.Options);

        var expectedPbipPath = Path.Combine(
            Path.GetDirectoryName(fixture.ReportRootPath)!,
            Path.GetFileName(fixture.ReportRootPath).Replace(".Report", string.Empty) + ".pbip");
        result.PbipFilePath.Should().Be(expectedPbipPath);
        File.Exists(result.PbipFilePath).Should().BeTrue();

        var pbipContent = File.ReadAllText(result.PbipFilePath);
        pbipContent.Should().Contain("\"version\": \"1.0\"");
        pbipContent.Should().Contain($"\"path\": \"{Path.GetFileName(fixture.ReportRootPath)}\"");
        pbipContent.Should().NotContain("SemanticModel");
    }

    [Fact]
    public void Run_ShouldThrow_WhenReportAndSemanticModelRootsHaveDifferentParents()
    {
        using var fixture = new PipelineTestFixture("Normal");
        var options = new PipelineOptions
        {
            DataDirectoryPath = fixture.Options.DataDirectoryPath,
            SemanticModelTemplateRootPath = fixture.Options.SemanticModelTemplateRootPath,
            SemanticModelRootPath = Path.Combine(Path.GetDirectoryName(fixture.SemanticModelRootPath)!, "nested", Path.GetFileName(fixture.SemanticModelRootPath)),
            ReportRootPath = fixture.Options.ReportRootPath,
            ReportTemplateRootPath = fixture.Options.ReportTemplateRootPath,
            SemanticModelRelativePath = fixture.Options.SemanticModelRelativePath,
            MetadataOutputPath = fixture.Options.MetadataOutputPath,
            ThrowOnValidationError = fixture.Options.ThrowOnValidationError,
            Logger = fixture.Options.Logger
        };
        var orchestrator = new PipelineOrchestrator();

        Action act = () => orchestrator.Run(options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot determine a single PBIP root directory*");
    }

    private static void EmitScheduledStartRelationshipDiagnostics(string semanticModelRootPath)
    {
        var relationshipsPath = Path.Combine(semanticModelRootPath, "definition", "relationships.tmdl");
        Console.WriteLine($"RELATIONSHIP-DIAGNOSTIC|Path={relationshipsPath}");
        if (!File.Exists(relationshipsPath))
        {
            Console.WriteLine("RELATIONSHIP-DIAGNOSTIC|Exists=false|Size=0");
            Console.WriteLine("RELATIONSHIP-DIAGNOSTIC|Newline=NONE");
            Console.WriteLine("RELATIONSHIP-DIAGNOSTIC|ScheduledStartBlock=<FILE-MISSING>");
            Console.WriteLine("RELATIONSHIP-DIAGNOSTIC|Pattern=<NOT-EVALUATED>");
            Console.WriteLine("RELATIONSHIP-DIAGNOSTIC|IsMatch=false");
            return;
        }

        var relationships = File.ReadAllText(relationshipsPath);
        var size = new FileInfo(relationshipsPath).Length;
        var newline = relationships.Contains("\r\n", StringComparison.Ordinal)
            ? "CRLF"
            : relationships.Contains("\n", StringComparison.Ordinal) ? "LF" : "NONE";
        const string relationshipId = "db2083da-0a18-4172-ab57-a096ce539554";
        const string localDateTable = "LocalDateTable_9043e032-67a4-45e7-bf88-28fec57966b9";
        const string factTable = "Fact_Pipeline_SampleData";
        const string columnName = "ScheduledStart";
        var relationshipPattern = $"relationship\\s+{Regex.Escape(relationshipId)}\\s+\\r?\\n\\s*joinOnDateBehavior:\\s*datePartOnly\\s+\\r?\\n\\s*fromColumn:\\s*{Regex.Escape(factTable)}\\.{Regex.Escape(columnName)}\\s+\\r?\\n\\s*toColumn:\\s*{Regex.Escape(localDateTable)}\\.Date";
        var blockMatch = Regex.Match(relationships, "relationship\\s+" + Regex.Escape(relationshipId) + ".*?(?=\\r?\\nrelationship\\s+|\\z)", RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var block = blockMatch.Success ? blockMatch.Value : "<NOT-FOUND>";
        var isMatch = Regex.IsMatch(relationships, relationshipPattern, RegexOptions.CultureInvariant);

        Console.WriteLine($"RELATIONSHIP-DIAGNOSTIC|Exists=true|Size={size}");
        Console.WriteLine($"RELATIONSHIP-DIAGNOSTIC|Newline={newline}");
        Console.WriteLine($"RELATIONSHIP-DIAGNOSTIC|ScheduledStartBlock={block.Replace("\r", "\\r").Replace("\n", "\\n")}");
        Console.WriteLine($"RELATIONSHIP-DIAGNOSTIC|Pattern={relationshipPattern}");
        Console.WriteLine($"RELATIONSHIP-DIAGNOSTIC|IsMatch={isMatch}");
    }
}

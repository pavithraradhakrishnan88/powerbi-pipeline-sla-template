using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class TemplateSemanticModelWriterTests
{
    [Fact]
    public void TemplateWriter_ShouldNotExposeObsoleteDataFolderPatcher()
    {
        // The last-known-good template intentionally does not use a DataFolder
        // parameter. The writer must preserve the template's existing partition
        // and source expressions instead of introducing a new DataFolder contract.
        typeof(TemplateSemanticModelWriter)
            .GetMethod("PatchDataFolderBytes")
            .Should().BeNull();
    }
}

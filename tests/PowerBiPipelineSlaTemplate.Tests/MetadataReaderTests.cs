using System;
using System.IO;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class MetadataReaderTests
{
    [Fact]
    public void Read_LoadsCurrentMetadataDocument()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "metadata", "metadata.json"));

        var result = new MetadataReader().Read(path);

        result.Project.Name.Should().NotBeNullOrWhiteSpace();
        result.Summary.TableCount.Should().BeGreaterThan(0);
        result.Tables.Should().HaveCountGreaterThan(0);
        result.Tables[0].Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Read_ThrowsWhenRequiredSectionIsMissing()
    {
        var path = Path.GetTempFileName();

        try
        {
            File.WriteAllText(path, """{"Tables":[]}""");

            var action = () => new MetadataReader().Read(path);

            action.Should().Throw<InvalidDataException>()
                .WithMessage("*Project*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_ThrowsWhenFileDoesNotExist()
    {
        var action = () => new MetadataReader().Read("missing-metadata.json");

        action.Should().Throw<FileNotFoundException>();
    }
}

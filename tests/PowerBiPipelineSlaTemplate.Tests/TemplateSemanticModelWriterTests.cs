using System;
using System.Text;
using FluentAssertions;
using PowerBiPipelineSlaTemplate.Core.Pbip;
using Xunit;

namespace PowerBiPipelineSlaTemplate.Tests;

public class TemplateSemanticModelWriterTests
{
    [Fact]
    public void PatchDataFolderBytes_ShouldUsePortableRelativeDataFolder()
    {
        const string original =
            "expression 'Other' = \r\n\t\tvalue = 1\r\n\r\n" +
            "expression DataFolder = \"C:\\\\agent\\\\_work\\\\1\\\\s\\\\data\" meta [IsParameterQuery=true, Type=\"Text\"]\r\n";

        var patchedBytes = TemplateSemanticModelWriter.PatchDataFolderBytes(Encoding.UTF8.GetBytes(original), "data");
        var patched = Encoding.UTF8.GetString(patchedBytes);

        patched.Should().Contain("expression DataFolder = \"data\" meta");
        patched.Should().NotContain("agent");
        patched.Should().NotContain("_work");
        patched.Should().Contain("\r\n");
    }

    [Fact]
    public void PatchDataFolderBytes_ShouldPreserveUtf8PreambleAndPatchExactBytes()
    {
        const string dataFolder = "data";
        var prefix = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble();
        var body = Encoding.UTF8.GetBytes("expression DataFolder = \"C:\\\\agent\\\\data\" meta [IsParameterQuery=true, Type=\"Text\"]\r\n");
        var originalBytes = new byte[prefix.Length + body.Length];
        Buffer.BlockCopy(prefix, 0, originalBytes, 0, prefix.Length);
        Buffer.BlockCopy(body, 0, originalBytes, prefix.Length, body.Length);

        var patchedBytes = TemplateSemanticModelWriter.PatchDataFolderBytes(originalBytes, dataFolder);

        patchedBytes.Should().StartWith(prefix);
        Encoding.UTF8.GetString(patchedBytes, prefix.Length, patchedBytes.Length - prefix.Length)
            .Should().Contain("expression DataFolder = \"data\" meta");
    }
}

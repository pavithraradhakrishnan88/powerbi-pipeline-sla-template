using PowerBiPipelineSlaTemplate.Core;

namespace PowerBiPipelineSlaTemplate.Tests.Shared.Fixtures;

public static class SampleModel
{
    public static ModelBuildResult Normal()
    {
        var builder = new ModelBuilder();
        return builder.Build(SampleMetadata.Normal());
    }

    public static ModelBuildResult WithValidationErrors()
    {
        return new ModelBuildResult
        {
            ValidationErrors =
            {
                "Relationship references missing source table: MissingTable",
                "Invalid measure definition for Total Duration"
            }
        };
    }
}

using System;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    /// <summary>
    /// Configures the pipeline orchestration process.
    /// </summary>
    public sealed class PipelineOptions
    {
        public string DataDirectoryPath { get; init; } = string.Empty;
        public string SemanticModelRootPath { get; init; } = string.Empty;
        public string SemanticModelTemplateRootPath { get; init; } = string.Empty;
        public string ReportRootPath { get; init; } = string.Empty;
        public string ReportTemplateRootPath { get; init; } = string.Empty;
        public string SemanticModelRelativePath { get; init; } = string.Empty;
        public bool ThrowOnValidationError { get; init; }
        public string MetadataOutputPath { get; init; } = string.Empty;
        public Action<string>? Logger { get; init; }
    }
}
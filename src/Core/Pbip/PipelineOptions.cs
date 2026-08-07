using System;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    /// <summary>
    /// Configures the pipeline orchestration process.
    /// </summary>
    public sealed class PipelineOptions
    {
        /// <summary>
        /// Gets or sets the source data directory used to extract metadata.
        /// </summary>
        public string DataDirectoryPath { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets the semantic model output directory.
        /// </summary>
        public string SemanticModelRootPath { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets the report output directory.
        /// </summary>
        public string ReportRootPath { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets the report template directory.
        /// </summary>
        public string ReportTemplateRootPath { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets the semantic model relative path used by the report pointer.
        /// </summary>
        public string SemanticModelRelativePath { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether metadata extraction should throw on validation errors.
        /// </summary>
        public bool ThrowOnValidationError { get; init; }

        /// <summary>
        /// Gets or sets the metadata JSON output file.
        /// </summary>
        public string MetadataOutputPath { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets an optional logger used during metadata extraction.
        /// </summary>
        public Action<string>? Logger { get; init; }
    }
}
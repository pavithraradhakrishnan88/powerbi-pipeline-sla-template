using System;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    /// <summary>
    /// Configures the pipeline orchestration process.
    /// </summary>
    public sealed class PipelineOptions
    {
        public string DataDirectoryPath { get; init; } = string.Empty;

        /// <summary>Generated semantic model output directory.</summary>

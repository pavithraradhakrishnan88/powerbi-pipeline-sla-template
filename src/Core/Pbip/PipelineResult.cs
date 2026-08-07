using PowerBiPipelineSlaTemplate.Core.Models;

namespace PowerBiPipelineSlaTemplate.Core.Pbip
{
    /// <summary>
    /// Represents the outcome of a pipeline run.
    /// </summary>
    public sealed class PipelineResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PipelineResult" /> class.
        /// </summary>
        /// <param name="metadata">The extracted metadata.</param>
        /// <param name="model">The built model.</param>
        /// <param name="semanticModelRootPath">The semantic model output directory.</param>
        /// <param name="reportRootPath">The report output directory.</param>
        public PipelineResult(DatabaseSchema metadata, ModelBuildResult model, string semanticModelRootPath, string reportRootPath)
        {
            Metadata = metadata;
            Model = model;
            SemanticModelRootPath = semanticModelRootPath;
            ReportRootPath = reportRootPath;
        }

        /// <summary>
        /// Gets the extracted metadata.
        /// </summary>
        public DatabaseSchema Metadata { get; }

        /// <summary>
        /// Gets the built model.
        /// </summary>
        public ModelBuildResult Model { get; }

        /// <summary>
        /// Gets the semantic model output directory.
        /// </summary>
        public string SemanticModelRootPath { get; }

        /// <summary>
        /// Gets the report output directory.
        /// </summary>
        public string ReportRootPath { get; }
    }
}
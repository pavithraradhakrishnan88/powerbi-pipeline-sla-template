using System.Threading;
using System.Threading.Tasks;

namespace PowerBiPipelineSlaTemplate.Core.Interfaces
{
    public interface IMetadataExtractor
    {
        string SourceName { get; }
        Task<Models.MetadataDocument> ExtractAsync(CancellationToken cancellationToken = default);
    }
}

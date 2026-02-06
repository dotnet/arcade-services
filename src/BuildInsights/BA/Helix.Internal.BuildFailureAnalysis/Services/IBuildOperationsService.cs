using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services
{
    public interface IBuildOperationsService
    {
        Task<bool> RetryBuild(string orgId, string projectId, int buildId, CancellationToken cancellationToken);
    }
}

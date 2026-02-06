using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.KnownIssues.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services
{
    public interface IBuildRetryService
    {
        Task<bool> RetryIfSuitable(string orgId, string projectId, int buildId, CancellationToken cancellationToken = default);
        Task<bool> RetryIfKnownIssueSuitable(string orgId, string projectId, int buildId, CancellationToken cancellationToken = default);

    }
}

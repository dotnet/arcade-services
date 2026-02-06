using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services;

public interface IPullRequestService
{
    bool IsPullRequestMessage(string message, out PullRequestData pullRequestData);
    Task ProcessPullRequestMessage(PullRequestData pullRequestData);
}

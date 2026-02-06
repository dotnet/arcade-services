using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Providers;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services;

public interface IAzDoToGitHubRepositoryService
{
    Task<AzDoToGitHubRepositoryResult> TryGetGitHubRepositorySupportingKnownIssues(BuildRepository buildRepository, string commit);
    Task<bool> IsInternalRepositorySupported(BuildRepository repository, string commit);
}

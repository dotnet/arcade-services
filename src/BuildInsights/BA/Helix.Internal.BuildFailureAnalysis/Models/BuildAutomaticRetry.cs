using Microsoft.Internal.Helix.GitHub.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

public class BuildAutomaticRetry
{
    public bool HasRerunAutomatically { get; }
    public GitHubIssue GitHubIssue { get; }

    public BuildAutomaticRetry() { }

    public BuildAutomaticRetry(bool hasRerunAutomatically)
    {
        HasRerunAutomatically = hasRerunAutomatically;
    }
    public BuildAutomaticRetry(bool hasRerunAutomatically,GitHubIssue gitHubIssue)
    {
        HasRerunAutomatically = hasRerunAutomatically;
        GitHubIssue = gitHubIssue;
    }
}

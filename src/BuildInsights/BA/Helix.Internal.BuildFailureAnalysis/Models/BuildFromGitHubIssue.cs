namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

public class BuildFromGitHubIssue
{
    public string OrganizationId { get; }
    public string ProjectId { get; }
    public int Id { get; }

    public BuildFromGitHubIssue(string organizationId, string projectId, int buildId)
    {
        OrganizationId = organizationId;
        ProjectId = projectId;
        Id = buildId;
    }
}

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class Repository
    {
        public string Id { get; }
        public bool HasIssues { get; }

        public Repository(string repositoryId, bool hasIssues)
        {
            Id = repositoryId;
            HasIssues = hasIssues;
        }
    }
}

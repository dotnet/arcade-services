using System.Collections.Generic;

namespace Microsoft.Internal.Helix.KnownIssues.Models
{
    public class GitHubIssuesSettings
    {
        public IEnumerable<string> CriticalIssuesRepositories { get; set; }
        public IEnumerable<string> CriticalIssuesLabels { get; set; }
        public IEnumerable<string> KnownIssuesRepositories { get; set; }
        public IEnumerable<string> KnownIssuesLabels { get; set; }
    }
}

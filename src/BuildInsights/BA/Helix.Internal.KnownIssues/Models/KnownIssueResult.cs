using System.Collections.Generic;

namespace Microsoft.Internal.Helix.KnownIssues.Models;

public class KnownIssueResult
{
    public int IssueId { get; set; }
    public string IssueRepository { get; set; }
    public KnownIssuesHits KnownIssuesHits { get; set; }
    public List<string> Labels { get; set; }
}

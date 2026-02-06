namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class PullRequestData
    {
        public string Action { get; set; }
        public bool Merged { get; set;  }
        public string Organization { get; set; }
        public string Repository { get; set; }
        public string HeadSha { get; set; }
        public long Number { get; set; }
    }
}

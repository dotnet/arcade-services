using System;
using System.Text.Json.Serialization;
using Microsoft.Internal.Helix.GitHub.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class CheckRunConclusionUpdateMessage
    {
        [JsonPropertyName("eventType")]
        public string EventType => "checkrun.conclusion-update";

        [JsonPropertyName("repository")]
        public string Repository { get; set; }

        [JsonPropertyName("issueNumber")]
        public int IssueNumber { get; set; }

        [JsonPropertyName("headSha")]
        public string HeadSha { get; set; }

        [JsonPropertyName("checkResult")]
        public string CheckResultString { get; set; }

        [JsonPropertyName("justification")]
        public string Justification { get; set; }

        public Octokit.CheckConclusion GetCheckConclusion()
        {
            return Enum.Parse<Octokit.CheckConclusion>(CheckResultString);
        }
    }
}

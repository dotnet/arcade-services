using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Microsoft.Internal.Helix.KnownIssues.Models
{
    public class AnalysisProcessRequest
    {
        [JsonPropertyName("issueId")]
        public long IssueId { get; set; }

        [JsonPropertyName("repository")]
        public string Repository { get; set; }
    }
}

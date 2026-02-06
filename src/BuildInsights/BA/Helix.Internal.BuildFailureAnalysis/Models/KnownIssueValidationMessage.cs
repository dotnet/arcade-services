using System.Text.Json.Serialization;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models;


public class KnownIssueValidationMessage
{
    [JsonPropertyName("eventType")]
    public string EventType => "knownissue.validate";
    [JsonPropertyName("organization")]
    public string Organization { get; set; }
    [JsonPropertyName("repository")]
    public string Repository { get; set; }
    [JsonPropertyName("issueId")]
    public int IssueId { get; set; }
    [JsonPropertyName("repositoryWithOwner")]
    public string RepositoryWithOwner { get; set; }
}

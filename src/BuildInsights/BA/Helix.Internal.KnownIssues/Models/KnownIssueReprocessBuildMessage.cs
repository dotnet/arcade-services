using System.Text.Json.Serialization;

namespace Microsoft.Internal.Helix.KnownIssues.Models;

public class KnownIssueReprocessBuildMessage
{
    [JsonPropertyName("eventType")]
    public string EventType => "knownissue.reprocessing";
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; }
    [JsonPropertyName("buildId")]
    public int BuildId { get; set; }
    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; }
}

using System.Text.Json.Serialization;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class RerunCheckRunAnalysisMessage
    {
        [JsonPropertyName("eventType")]
        public string EventType { get; set; }

        [JsonPropertyName("repository")]
        public string Repository { get; set; }

        [JsonPropertyName("headSha")]
        public string HeadSha { get; set; }
    }
}

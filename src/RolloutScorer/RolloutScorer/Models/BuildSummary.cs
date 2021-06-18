using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace RolloutScorer.Models
{
    public class BuildSummary
    {
        [JsonProperty("buildNumber")]
        public string BuildNumber { get; set; }
        [JsonProperty("_links")]
        public BuildLinks Links { get; set; } = new BuildLinks();
        [JsonProperty("result")]
        public string Result { get; set; }
        [JsonProperty("queueTime")]

        public DateTimeOffset QueueTime { get; set; }
        [JsonProperty("startTime")]
        public DateTimeOffset StartTime { get; set; }
        [JsonProperty("finishTime")]
        public DateTimeOffset FinishTime { get; set; }

        [JsonIgnore]
        public bool DeploymentReached { get; set; } = false;
        [JsonIgnore]
        public List<BuildTimelineEntry> Stages { get; set; } = new List<BuildTimelineEntry>();

        [JsonIgnore]
        public string SelfLink => Links.SelfLink.Href;
        [JsonIgnore]
        public string WebLink => Links.WebLink.Href;
        [JsonIgnore]
        public string SourceLink => Links.SourceLink.Href;
        [JsonIgnore]
        public string TimelineLink => Links.TimelineLink.Href;
    }
}

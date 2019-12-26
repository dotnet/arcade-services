using Newtonsoft.Json;
using System.Collections.Generic;

namespace RolloutScorer
{
    public class BuildTimeline
    {
        [JsonProperty("records")]
        public List<BuildTimelineEntry> Records { get; set; }
    }

    public class BuildTimelineEntry
    {
        [JsonProperty("previousAttempts")]
        public List<PreviousAttempt> PreviousAttempts { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("startTime")]
        public string StartTime { get; set; }
        [JsonProperty("finishTime")]
        public string EndTime { get; set; }
    }

    public class PreviousAttempt
    {
        [JsonProperty("attempt")]
        public int Attempt { get; set; }
        [JsonProperty("timelineId")]
        public string TimelineId { get; set; }
    }
}

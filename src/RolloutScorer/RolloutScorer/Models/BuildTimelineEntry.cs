using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace RolloutScorer.Models
{
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
}

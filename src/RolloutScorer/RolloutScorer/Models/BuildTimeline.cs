using Newtonsoft.Json;
using System.Collections.Generic;

namespace RolloutScorer.Models
{
    public class BuildTimeline
    {
        [JsonProperty("records")]
        public List<BuildTimelineEntry> Records { get; set; }
    }
}

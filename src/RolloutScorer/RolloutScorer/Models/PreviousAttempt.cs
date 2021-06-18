using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace RolloutScorer.Models
{
    public class PreviousAttempt
    {
        [JsonProperty("attempt")]
        public int Attempt { get; set; }
        [JsonProperty("timelineId")]
        public string TimelineId { get; set; }
    }
}

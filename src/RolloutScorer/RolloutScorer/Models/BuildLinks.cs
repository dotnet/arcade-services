using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace RolloutScorer.Models
{
    public class BuildLinks
    {
        [JsonProperty("self")]
        public BuildLink SelfLink { get; set; } = new BuildLink();
        [JsonProperty("web")]
        public BuildLink WebLink { get; set; } = new BuildLink();
        [JsonProperty("sourceVersionDisplayUri")]
        public BuildLink SourceLink { get; set; } = new BuildLink();
        [JsonProperty("timeline")]
        public BuildLink TimelineLink { get; set; } = new BuildLink();
    }
}

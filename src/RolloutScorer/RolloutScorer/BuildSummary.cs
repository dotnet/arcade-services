using Newtonsoft.Json;

namespace RolloutScorer
{
    public class BuildSummary
    {
        [JsonProperty("buildNumber")]
        public string BuildNumber { get; set; }
        [JsonProperty("_links")]
        public BuildLinks Links { get; set; }
        [JsonProperty("result")]
        public string Result { get; set; }


        [JsonIgnore]
        public bool DeploymentReached { get; set; } = false;
        [JsonIgnore]
        public string SelfLink => Links.SelfLink.Href;
        [JsonIgnore]
        public string WebLink => Links.WebLink.Href;
        [JsonIgnore]
        public string SourceLink => Links.SourceLink.Href;
        [JsonIgnore]
        public string TimelineLink => Links.TimelineLink.Href;
    }

    public class BuildLinks
    {
        [JsonProperty("self")]
        public BuildLink SelfLink { get; set; }
        [JsonProperty("web")]
        public BuildLink WebLink { get; set; }
        [JsonProperty("sourceVersionDisplayUri")]
        public BuildLink SourceLink { get; set; }
        [JsonProperty("timeline")]
        public BuildLink TimelineLink { get; set; }
    }

    public class BuildLink
    {
        [JsonProperty("href")]
        public string Href { get; set; }
    }
}

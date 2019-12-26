using Newtonsoft.Json;


namespace RolloutScorer
{
    public class BuildSource
    {
        [JsonProperty("comment")]
        public string Comment { get; set; }
    }
}

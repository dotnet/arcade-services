using Newtonsoft.Json;

namespace RolloutScorer.Models
{
    public class BuildSource
    {
        [JsonProperty("comment")]
        public string Comment { get; set; }
    }
}

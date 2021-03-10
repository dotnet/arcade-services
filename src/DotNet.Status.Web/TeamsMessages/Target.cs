using Newtonsoft.Json;

namespace DotNet.Status.Web.TeamsMessages
{
    public class Target
    {
        [JsonProperty("os")]
        public string OperatingSystem { get; set; } = "default";

        public string Uri { get; set; }
    }
}
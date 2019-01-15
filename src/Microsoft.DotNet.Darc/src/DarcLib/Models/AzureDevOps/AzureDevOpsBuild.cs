using Newtonsoft.Json;

namespace Microsoft.DotNet.DarcLib
{
    public class AzureDevOpsBuild
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("buildNumber")]
        public string BuildNumber { get; set; }

        [JsonProperty("definition")]
        public AzureDevOpsBuildDefinition Definition { get; set; }

        [JsonProperty("project")]
        public AzureDevOpsProject Project { get; set; }
    }
}

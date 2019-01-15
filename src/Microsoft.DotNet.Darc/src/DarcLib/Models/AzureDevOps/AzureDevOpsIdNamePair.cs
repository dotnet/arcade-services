using Newtonsoft.Json;

namespace Microsoft.DotNet.DarcLib
{
    public partial class AzureDevOpsIdNamePair
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }
}

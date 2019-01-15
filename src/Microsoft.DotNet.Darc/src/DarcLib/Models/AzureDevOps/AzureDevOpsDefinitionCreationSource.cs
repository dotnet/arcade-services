using Newtonsoft.Json;

namespace Microsoft.DotNet.DarcLib
{
    public partial class AzureDevOpsDefinitionCreationSource
    {
        [JsonProperty("$type")]
        public string Type { get; set; }

        [JsonProperty("$value")]
        public string Value { get; set; }
    }
}

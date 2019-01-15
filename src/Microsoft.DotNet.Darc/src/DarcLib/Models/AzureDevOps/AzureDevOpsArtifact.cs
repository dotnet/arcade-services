using Newtonsoft.Json;

namespace Microsoft.DotNet.DarcLib
{
    public partial class AzureDevOpsArtifact
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("alias")]
        public string Alias { get; set; }

        [JsonProperty("definitionReference")]
        public AzureDevOpsArtifactSourceReference DefinitionReference { get; set; }
    }
}

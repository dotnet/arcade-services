using Newtonsoft.Json;

namespace Microsoft.DotNet.DarcLib
{
    public partial class AzureDevOpsArtifactSourceReference
    {
        [JsonProperty("defaultVersionSpecific")]
        public AzureDevOpsIdNamePair DefaultVersionSpecific { get; set; }

        [JsonProperty("defaultVersionType")]
        public AzureDevOpsIdNamePair DefaultVersionType { get; set; }

        [JsonProperty("definition")]
        public AzureDevOpsIdNamePair Definition { get; set; }

        [JsonProperty("project")]
        public AzureDevOpsIdNamePair Project { get; set; }
    }
}

using Newtonsoft.Json;

namespace Microsoft.DotNet.DarcLib
{
    public class AzureDevOpsArtifactSourceReference
    {
        public AzureDevOpsIdNamePair DefaultVersionSpecific { get; set; }

        public AzureDevOpsIdNamePair DefaultVersionType { get; set; }

        public AzureDevOpsIdNamePair Definition { get; set; }

        public AzureDevOpsIdNamePair Project { get; set; }
    }
}

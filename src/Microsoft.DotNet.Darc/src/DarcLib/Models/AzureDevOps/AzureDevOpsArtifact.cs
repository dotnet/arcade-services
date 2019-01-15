using Newtonsoft.Json;

namespace Microsoft.DotNet.DarcLib
{
    public class AzureDevOpsArtifact
    {
        public string Type { get; set; }

        public string Alias { get; set; }

        public AzureDevOpsArtifactSourceReference DefinitionReference { get; set; }
    }
}

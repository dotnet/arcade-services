using Newtonsoft.Json;

namespace Microsoft.DotNet.DarcLib
{
    public class AzureDevOpsBuild
    {
        public long Id { get; set; }

        public string BuildNumber { get; set; }

        public AzureDevOpsBuildDefinition Definition { get; set; }

        public AzureDevOpsProject Project { get; set; }
    }
}

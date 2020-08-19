using System.Collections.Generic;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    public class ServiceFabricNodeType
    {
        // These properties are filled by the config system
#pragma warning disable 8618 // Non-nullable property is uninitialized
        public string Name { get; set; }
        public List<int> Ports { get; set; }
        public int InstanceCount { get; set; }
        public ResourceReference UserAssignedIdentity { get; set; }
        public string Sku { get; set; }
        public ServiceFabricNodeVmImage VmImage { get; set; }
#pragma warning restore 8618
    }
}

using System.Collections.Generic;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    public class ServiceFabricNodeType
    {
        public string Name { get; set; }

        public List<int> Ports { get; set; }
        public int InstanceCount { get; set; }
        public ResourceReference UserAssignedIdentity { get; set; }
        public string Sku { get; set; }
        public ServiceFabricNodeVmImage VmImage { get; set; }
    }
}
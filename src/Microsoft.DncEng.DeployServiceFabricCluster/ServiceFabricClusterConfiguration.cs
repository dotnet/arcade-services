using System;
using System.Collections.Generic;
using System.Linq;

// These types must have public {get; set;} properties, those make nullable act weird
#nullable disable

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    public class ServiceFabricClusterConfiguration
    {
        public void Validate()
        {
            if (NodeTypes.All(nt => nt.Name != "Primary"))
            {
                throw new ArgumentException("Must have a single node type named 'Primary'");
            }
            NodeTypes = NodeTypes.OrderBy(nt => nt.Name == "Primary" ? 0 : 1).ToList();
        }

        public string Name { get; set; }

        public string Location { get; set; }

        public string ResourceGroup { get; set; }

        public Guid SubscriptionId { get; set; }

        public string AdminUsername { get; set; }
        public string AdminPassword { get; set; }

        public int TcpGatewayPort { get; set; } = 19000;
        public int HttpGatewayPort { get; set; } = 19080;

        public List<ServiceFabricNodeType> NodeTypes { get; set; }
        public string CertificateCommonName { get; set; }
        public string AdminClientCertificateCommonName { get; set; }
        public string AdminClientCertificateIssuerThumbprint { get; set; }
        public string CertificateSourceVaultId { get; set; }
        public List<string> CertificateUrls { get; set; }
    }

    public class ServiceFabricNodeType
    {
        public string Name { get; set; }

        public List<ServiceFabricNodeEndpoint> Endpoints { get; set; }
        public int InstanceCount { get; set; }
        public string UserAssignedIdentityId { get; set; }
        public string Sku { get; set; }
        public ServiceFabricNodeVmImage VmImage { get; set; }
    }

    public class ServiceFabricNodeVmImage
    {
        public string Publisher { get; set; }
        public string Offer { get; set; }
        public string Sku { get; set; }
        public string Version { get; set; }
    }

    public class ServiceFabricNodeEndpoint
    {
        public int ExternalPort { get; set; }
        public int InternalPort { get; set; }
    }
}

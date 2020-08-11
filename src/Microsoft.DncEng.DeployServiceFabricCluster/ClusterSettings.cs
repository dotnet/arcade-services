using System;
using System.Collections.Generic;
using System.Linq;

// These types must have public {get; set;} properties, those make nullable act weird
#nullable disable

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    public class ClusterSettings : ResourceGroupDeployerSettings
    {
        public override void Validate()
        {
            if (NodeTypes.All(nt => nt.Name != "Primary"))
            {
                throw new ArgumentException("Must have a single node type named 'Primary'");
            }
            NodeTypes = NodeTypes.OrderBy(nt => nt.Name == "Primary" ? 0 : 1).ToList();

            base.Validate();
        }

        public string AdminUsername { get; set; }
        public string AdminPassword { get; set; }

        public List<ServiceFabricNodeType> NodeTypes { get; set; }
        public string AdminClientCertificateCommonName { get; set; }
        public string AdminClientCertificateIssuerThumbprint { get; set; }

        public string SslCertificateCommonName { get; set; }
        public List<string> Certificates { get; set; }

        public ResourceReference Gateway { get; set; }
        public ResourceReference VNet { get; set; }
        public int ClusterIndex { get; set; }
    }
}

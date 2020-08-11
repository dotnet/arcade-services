using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    public class GatewaySettings : ResourceGroupDeployerSettings
    {
        public Dictionary<string, string>? NeededSecurityGroupRules
        {
            get
            {
                if (ExternalPorts == null)
                {
                    return null;
                }
                return new[]
                    {
                        ("AppGatewayRule", "65200-65535"),
                        ("ServiceFabricTcp", ServiceFabricConstants.TcpGatewayPort.ToString()),
                        ("ServiceFabricHttp", ServiceFabricConstants.HttpGatewayPort.ToString()),
                    }.Concat(ExternalPorts
                        .Distinct()
                        .Select((p, i) =>
                            ("SslEndpoint-" + p, p.ToString())))
                    .ToDictionary(t => t.Item1, t => t.Item2);
            }
        }

        public ResourceReference UserAssignedIdentity { get; set; }
        public List<int> ExternalPorts { get; set; }
        public string SslCertificateName { get; set; }
    }
}

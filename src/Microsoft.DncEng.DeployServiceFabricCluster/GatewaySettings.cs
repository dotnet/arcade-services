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

                var result = new Dictionary<string, string>
                {
                    {"AppGatewayRule", "65200-65535"},
                    {"ServiceFabricTcp", ServiceFabricConstants.TcpGatewayPort.ToString()},
                    {"ServiceFabricHttp", ServiceFabricConstants.HttpGatewayPort.ToString()},
                };
                foreach (var port in ExternalPorts)
                {
                    result[$"SslEndpoint-{port}"] = port.ToString();
                }

                return result;
            }
        }

        // These properties are filled by the config system
#pragma warning disable 8618 // Non-nullable property is uninitialized
        public ResourceReference UserAssignedIdentity { get; set; }
        public List<int> ExternalPorts { get; set; }
        public string SslCertificateName { get; set; }
#pragma warning restore 8618
    }
}

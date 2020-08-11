using System;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    public class ResourceGroupDeployerSettings
    {
        public virtual void Validate()
        {
        }

        public string Name { get; set; }
        public string Location { get; set; }
        public string ResourceGroup { get; set; }
        public Guid SubscriptionId { get; set; }
        public ResourceReference CertificateSourceVault { get; set; }
    }
}

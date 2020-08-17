using System;

namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    public class ResourceGroupDeployerSettings
    {
        public virtual void Validate()
        {
        }

        // These properties are filled by the config system
#pragma warning disable 8618 // Non-nullable property is uninitialized
        public string Name { get; set; }
        public string Location { get; set; }
        public string ResourceGroup { get; set; }
        public Guid SubscriptionId { get; set; }
        public ResourceReference CertificateSourceVault { get; set; }
#pragma warning restore 8618
    }
}

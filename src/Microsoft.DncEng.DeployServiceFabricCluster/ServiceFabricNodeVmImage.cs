namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    public class ServiceFabricNodeVmImage
    {
        // These properties are filled by the config system
#pragma warning disable 8618 // Non-nullable property is uninitialized
        public string Publisher { get; set; }
        public string Offer { get; set; }
        public string Sku { get; set; }
        public string Version { get; set; }
#pragma warning restore 8618
    }
}

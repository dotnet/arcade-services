namespace Microsoft.DncEng.DeployServiceFabricCluster
{
    public class ResourceReference
    {
        // These properties are filled by the config system
#pragma warning disable 8618 // Non-nullable property is uninitialized
        public string ResourceGroup { get; set; }
        public string Name { get; set; }
        public string SubscriptionId { get; set; }
#pragma warning restore 8618
    }
}

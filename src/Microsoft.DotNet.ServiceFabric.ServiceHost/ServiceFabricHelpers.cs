using System;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public static class ServiceFabricHelpers
    {
        public static bool RunningInServiceFabric()
        {
            string fabricApplication = Environment.GetEnvironmentVariable("Fabric_ApplicationName");
            return !string.IsNullOrEmpty(fabricApplication);
        }
    }
}

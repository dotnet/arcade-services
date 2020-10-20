namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public class EmptyServiceLoadReporter : IServiceLoadReporter
    {
        public void ReportLoad(string name, int value)
        {
        }
    }
}

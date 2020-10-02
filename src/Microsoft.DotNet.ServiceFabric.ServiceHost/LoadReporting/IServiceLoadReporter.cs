namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public interface IServiceLoadReporter
    {
        void ReportLoad(string name, int value);
    }
}

using System.Fabric;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public class StatelessServiceLoadReporter : IServiceLoadReporter
    {
        private readonly IStatelessServicePartition _partition;

        public StatelessServiceLoadReporter(IStatelessServicePartition partition)
        {
            _partition = partition;
        }

        public void ReportLoad(string name, int value)
        {
            _partition.ReportLoad(new[] {new LoadMetric(name, value)});
        }
    }
}

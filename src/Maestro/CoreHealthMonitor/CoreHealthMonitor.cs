using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Options;

namespace CoreHealthMonitor
{
    public class DriveMonitorOptions
    {
        public long MinimumFreeSpaceBytes { get; set; }
    }

    /// <summary>
    /// An instance of this class is created for each service instance by the Service Fabric runtime.
    /// </summary>
    internal sealed class CoreHealthMonitorService : IServiceImplementation
    {
        private readonly IOptions<DriveMonitorOptions> _driveOptions;

        public CoreHealthMonitorService(
            IInstanceHealthReporter<CoreHealthMonitorService> health,
            IOptions<DriveMonitorOptions> driveOptions)
        {
            _driveOptions = driveOptions;
        }

        public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                
            }

            return TimeSpan.FromMinutes(5);
        }
    }
}

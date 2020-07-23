// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Fabric;
using System.Fabric.Health;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Internal.Health
{
    public class ServiceFabricHealthReportProvider : IHealthReportProvider
    {
        private readonly StatelessServiceContext _statelessServiceContext;
        private readonly FabricClient _fabricClient;

        private readonly StatefulServiceContext _statefulServiceContext;

        public ServiceFabricHealthReportProvider(StatefulServiceContext statefulServiceContext) : this()
        {
            _statefulServiceContext = statefulServiceContext;
        }

        private ServiceFabricHealthReportProvider(StatelessServiceContext statelessServiceContext) : this()
        {
            _statelessServiceContext = statelessServiceContext;
        }

        private ServiceFabricHealthReportProvider()
        {
            _fabricClient = new FabricClient();
        }

        public Task UpdateStatusAsync(string serviceName, string subStatusName, HealthStatus status, string message)
        {
            var healthInfo = new HealthInformation(
                GetType().FullName,
                subStatusName,
                MapStatus(status))
            {
                Description = message
            };

            HealthReport report;
            if (_statefulServiceContext != null)
            {
                report = new PartitionHealthReport(_statefulServiceContext.PartitionId, healthInfo);
            }
            else
            {
                report = new StatelessServiceInstanceHealthReport(_statelessServiceContext.PartitionId, _statelessServiceContext.InstanceId, healthInfo);
            }

            _fabricClient.HealthManager.ReportHealth(report, new HealthReportSendOptions {Immediate = true});
            return Task.CompletedTask;
        }

        private static HealthState MapStatus(HealthStatus status)
        {
            switch (status)
            {
                case HealthStatus.Healthy:
                    return HealthState.Ok;
                case HealthStatus.Warning:
                    return HealthState.Warning;
                case HealthStatus.Error:
                    return HealthState.Error;
                default:
                    return HealthState.Invalid;
            }
        }
    }
}

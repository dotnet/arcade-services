// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Health;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.Internal.Health
{
    public class ServiceFabricHealthReportOptions
    {
        public string ServiceUri { get; set; }
    }

    public class ServiceFabricHealthReportProvider : IHealthReportProvider
    {
        private readonly IOptions<ServiceFabricHealthReportOptions> _options;
        private readonly ServiceContext _context;
        private readonly FabricClient _fabricClient;
        private readonly bool _isStateful;

        public ServiceFabricHealthReportProvider(StatefulServiceContext statefulServiceContext, IOptions<ServiceFabricHealthReportOptions> options) : this(statefulServiceContext, options, true)
        {
        }

        public ServiceFabricHealthReportProvider(StatelessServiceContext statelessServiceContext, IOptions<ServiceFabricHealthReportOptions> options) : this(statelessServiceContext, options, false)
        {
        }

        private ServiceFabricHealthReportProvider(ServiceContext context, IOptions<ServiceFabricHealthReportOptions> options, bool isStateful)
        {
            _context = context;
            _options = options;
            _isStateful = isStateful;
            _fabricClient = new FabricClient();
        }

        private Uri GetServiceUri()
        {
            string optionsUri = _options.Value.ServiceUri;
            if (!string.IsNullOrEmpty(optionsUri))
                return new Uri(optionsUri);

            return _context.ServiceName;
        }

        public Task UpdateStatusAsync(string serviceName, string instance, string subStatusName, HealthStatus status, string message)
        {
            var healthInfo = new HealthInformation(
                GetType().FullName,
                subStatusName,
                MapStatus(status))
            {
                Description = message
            };

            System.Fabric.Health.HealthReport report;
            if (instance == null)
            {
                report = new ServiceHealthReport(
                    GetServiceUri(),
                    healthInfo
                );
            }
            else if (_isStateful)
            {
                report = new StatefulServiceReplicaHealthReport(
                    _context.PartitionId,
                    _context.ReplicaOrInstanceId,
                    healthInfo
                );
            }
            else
            {
                report = new StatelessServiceInstanceHealthReport(
                    _context.PartitionId,
                    _context.ReplicaOrInstanceId,
                    healthInfo
                );
            }

            _fabricClient.HealthManager.ReportHealth(report, new HealthReportSendOptions {Immediate = true});
            return Task.CompletedTask;
        }

        public Task<HealthReport> GetStatusAsync(string serviceName, string instance, string subStatusName)
        {
            return Task.FromResult<HealthReport>(null);
        }

        public Task<IList<HealthReport>> GetAllStatusAsync(string serviceName)
        {
            return Task.FromResult<IList<HealthReport>>(null);
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

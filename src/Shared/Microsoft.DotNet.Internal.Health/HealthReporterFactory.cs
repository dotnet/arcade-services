// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Internal.Health
{
    public class HealthReporterFactory : IHealthReporterFactory
    {
        private readonly IInstanceAccessor _instance;
        private readonly IEnumerable<IHealthReportProvider> _providers;

        public HealthReporterFactory(
            IInstanceAccessor instance,
            IEnumerable<IHealthReportProvider> providers)
        {
            _instance = instance;
            _providers = providers;
        }

        private class ExternalHealthReport : IExternalHealthReporter
        {
            private readonly HealthReporterFactory _factory;
            private readonly string _serviceName;
            private readonly string _instance;

            public ExternalHealthReport(HealthReporterFactory factory, string serviceName, string instance = null)
            {
                _factory = factory;
                _serviceName = serviceName;
                _instance = instance;
            }

            public Task UpdateStatusAsync(string subStatus, HealthStatus status, string message)
            {
                return _factory.UpdateStatusAsync(_serviceName, _instance, subStatus, status, message);
            }

            public Task<HealthReport> GetHealth(string subStatus)
            {
                return _factory.GetStatusAsync(_serviceName, _instance, subStatus);
            }
        }

        private async Task<HealthReport> GetStatusAsync(string serviceName, string instance, string subStatus)
        {
            var results = await Task.WhenAll(_providers.Select(p => p.GetStatusAsync(serviceName, instance, subStatus)));
            return results.FirstOrDefault(r => r != null);
        }

        private Task UpdateStatusAsync(
            string serviceName,
            string instance,
            string subStatusName,
            HealthStatus status,
            string message)
        {
            return Task.WhenAll(_providers.Select(p => p.UpdateStatusAsync(serviceName, instance, subStatusName, status, message)));
        }

        private class ServiceHealthReport<T> : IServiceHealthReporter<T>
        {
            private readonly HealthReporterFactory _factory;

            public ServiceHealthReport(HealthReporterFactory factory)
            {
                _factory = factory;
            }

            public Task UpdateStatusAsync(string subStatus, HealthStatus status, string message)
            {
                return _factory.UpdateStatusAsync(typeof(T).FullName, null, subStatus, status, message);
            }
        }

        private class InstanceHealthReport<T> : IInstanceHealthReporter<T>
        {
            private readonly HealthReporterFactory _factory;

            public InstanceHealthReport(HealthReporterFactory factory)
            {
                _factory = factory;
            }

            public async Task UpdateStatusAsync(string subStatus, HealthStatus status, string message)
            {
                await _factory.UpdateStatusAsync(
                    typeof(T).FullName,
                    _factory._instance.GetCurrentInstanceName(),
                    subStatus,
                    status,
                    message
                );
            }
        }

        public IServiceHealthReporter<T> ForService<T>()
        {
            return new ServiceHealthReport<T>(this);
        }

        public IInstanceHealthReporter<T> ForInstance<T>()
        {
            return new InstanceHealthReport<T>(this);
        }
        
        public IExternalHealthReporter ForExternal(string serviceName)
        {
            return new ExternalHealthReport(this, serviceName);
        }

        public IExternalHealthReporter ForExternalInstance(string serviceName, string instance)
        {
            return new ExternalHealthReport(this, serviceName, instance);
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.DotNet.Internal.Health
{
    public static class HealthReportExtensions
    {
        public static IServiceCollection AddHealthReporting(this IServiceCollection services, Action<HealthReportingBuilder> configure)
        {
            services.AddTransient(typeof(IInstanceHealthReporter<>), typeof(InstanceWrapper<>));
            services.AddTransient(typeof(IServiceHealthReporter<>), typeof(ServiceWrapper<>));
            services.TryAddSingleton<IHealthReporterFactory, HealthReporterFactory>();
            services.TryAddSingleton<IInstanceAccessor, InstanceAccessor>();
            configure(new HealthReportingBuilder(services));
            return services;
        }

        private class InstanceWrapper<T> : IInstanceHealthReporter<T>
        {
            private readonly IInstanceHealthReporter<T> _impl;

            public InstanceWrapper(IHealthReporterFactory factory)
            {
                _impl = factory.ForInstance<T>();
            }

            public Task UpdateStatusAsync(string subStatus, HealthStatus status, string message)
            {
                return _impl.UpdateStatusAsync(subStatus, status, message);
            }
        }
        

        private class ServiceWrapper<T> : IServiceHealthReporter<T>
        {
            private readonly IServiceHealthReporter<T> _impl;
            public ServiceWrapper(IHealthReporterFactory factory)
            {
                _impl = factory.ForService<T>();
            }

            public Task UpdateStatusAsync(string subStatus, HealthStatus status, string message)
            {
                return _impl.UpdateStatusAsync(subStatus, status, message);
            }
        }
    }
}

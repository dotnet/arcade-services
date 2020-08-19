// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Internal.Health
{
    public sealed class LogHealthReporter : IHealthReportProvider
    {
        private readonly ILogger<LogHealthReporter> _logger;

        public LogHealthReporter(ILogger<LogHealthReporter> logger)
        {
            _logger = logger;
        }

        public Task UpdateStatusAsync(string serviceName, string instance, string subStatusName, HealthStatus status, string message)
        {
            LogLevel level = status switch
            {
                HealthStatus.Invalid => LogLevel.Critical,
                HealthStatus.Healthy => LogLevel.Trace,
                HealthStatus.Warning => LogLevel.Warning,
                HealthStatus.Error => LogLevel.Error,
                HealthStatus.Unknown => LogLevel.Critical,
                _ => LogLevel.Critical
            };

            _logger.Log(
                level,
                "Health report {subStatusName} for service {serviceName}/{instance} is {status}, message: {message}",
                subStatusName,
                serviceName,
                instance,
                status,
                message
            );

            return Task.CompletedTask;
        }

        public Task<HealthReport> GetStatusAsync(string serviceName, string instance, string subStatusName)
        {
            return Task.FromResult((HealthReport) null);
        }

        public Task<IList<HealthReport>> GetAllStatusAsync(string serviceName)
        {
            return Task.FromResult((IList<HealthReport>) null);
        }
    }
}

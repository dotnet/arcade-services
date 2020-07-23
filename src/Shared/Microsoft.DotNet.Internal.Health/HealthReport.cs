// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Internal.Health
{
    public class HealthReport<T> : IHealthReport<T>
    {
        private readonly IEnumerable<IHealthReportProvider> _providers;
        private readonly string _serviceName;

        public HealthReport(IEnumerable<IHealthReportProvider> providers)
        {
            _providers = providers;
            _serviceName = typeof(T).FullName;
        }

        public Task UpdateStatus(string subStatusName, HealthStatus status, string message)
        {
            return Task.WhenAll(_providers.Select(p => p.UpdateStatusAsync(_serviceName, subStatusName, status, message)));
        }
    }
}

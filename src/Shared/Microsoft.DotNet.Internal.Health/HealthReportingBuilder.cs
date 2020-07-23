// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Internal.Health
{
    public class HealthReportingBuilder
    {
        private readonly IServiceCollection _services;

        public HealthReportingBuilder(IServiceCollection services)
        {
            _services = services;
        }
        
        public HealthReportingBuilder AddAzureTable(string statusTableUrl)
        {
            return AddAzureTable(o => o.WriteSasUri = statusTableUrl);
        }

        public HealthReportingBuilder AddAzureTable(Action<AzureTableHealthReportingOptions> configure)
        {
            _services.AddSingleton<IHealthReportProvider, AzureTableHealthReportProvider>();
            _services.Configure(configure);
            return this;
        }

        public HealthReportingBuilder AddServiceFabric()
        {
            _services.AddSingleton<IHealthReportProvider, ServiceFabricHealthReportProvider>();
            return this;
        }
    }
}

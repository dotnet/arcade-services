// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.Extensions.Configuration;
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
            return AddAzureTable((o, _) => o.WriteSasUri = statusTableUrl);
        }

        public HealthReportingBuilder AddAzureTable(Action<AzureTableHealthReportingOptions, IServiceProvider> configure)
        {
            AddProvider<AzureTableHealthReportProvider, AzureTableHealthReportingOptions>(configure);
            return this;
        }

        public HealthReportingBuilder AddServiceFabric()
        {
            AddProvider<ServiceFabricHealthReportProvider>();
            return this;
        }

        public HealthReportingBuilder AddServiceFabric(Action<ServiceFabricHealthReportOptions, IServiceProvider> configure)
        {
            AddProvider<ServiceFabricHealthReportProvider, ServiceFabricHealthReportOptions>(configure);
            return this;
        }

        public HealthReportingBuilder AddLogging()
        {
            AddProvider<LogHealthReporter>();
            return this;
        }

        public HealthReportingBuilder AddProvider<T>() where T : class, IHealthReportProvider
        {
            _services.AddSingleton<T>();
            _services.AddSingleton<IHealthReportProvider>(p => p.GetRequiredService<T>());
            return this;
        }

        public HealthReportingBuilder AddProvider<T>(T provider) where T : class, IHealthReportProvider
        {
            _services.AddSingleton(provider);
            _services.AddSingleton<IHealthReportProvider>(provider);
            return this;
        }

        public HealthReportingBuilder AddProvider<TProvider, TOptions>(Action<TOptions, IServiceProvider> configure)
            where TProvider : class, IHealthReportProvider
            where TOptions : class
        {
            _services.AddSingleton<TProvider>();
            _services.AddSingleton<IHealthReportProvider>(p => p.GetRequiredService<TProvider>());
            _services.Configure(configure);
            return this;
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Fabric;
using JetBrains.Annotations;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Services.Remoting;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    [PublicAPI]
    public static class ServiceFabricServiceCollectionExtensions
    {
        /// <summary>
        ///     Adds a service fabric service remoting proxy to the IServiceCollection if an implementation of the interface
        ///     doesn't already exist.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="services"></param>
        /// <param name="serviceUri"></param>
        /// <returns></returns>
        public static IServiceCollection TryAddServiceFabricService<TService>(
            this IServiceCollection services,
            string serviceUri) where TService : class, IService
        {
            services.TryAddSingleton(
                provider => ServiceHostProxy.Create<TService>(
                    new Uri(serviceUri),
                    provider.GetRequiredService<TelemetryClient>(),
                    provider.GetRequiredService<ServiceContext>()));
            return services;
        }

        /// <summary>
        ///     Adds a service fabric service remoting proxy to the IServiceCollection
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="services"></param>
        /// <param name="serviceUri"></param>
        /// <returns></returns>
        public static IServiceCollection AddServiceFabricService<TService>(
            this IServiceCollection services,
            string serviceUri) where TService : class, IService
        {
            services.AddSingleton(
                provider => ServiceHostProxy.Create<TService>(
                    new Uri(serviceUri),
                    provider.GetRequiredService<TelemetryClient>(),
                    provider.GetRequiredService<ServiceContext>()));
            return services;
        }
    }
}

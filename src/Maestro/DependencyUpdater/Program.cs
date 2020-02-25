// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Contracts;
using Maestro.Data;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyUpdater
{
    internal static class Program
    {
        /// <summary>
        ///     This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            ServiceHost.Run(
                host =>
                {
                    host.RegisterStatefulService<DependencyUpdater>("DependencyUpdaterType");
                    host.ConfigureContainer(builder => { builder.AddServiceFabricActor<ISubscriptionActor>(); });
                    host.ConfigureServices(
                        services =>
                        {
                            services.AddDefaultJsonConfiguration();
                            services.AddBuildAssetRegistry(
                                (provider, options) =>
                                {
                                    var config = provider.GetRequiredService<IConfiguration>();
                                    options.UseSqlServer(config.GetSection("BuildAssetRegistry")["ConnectionString"]);
                                });
                        });
                });
        }
    }
}

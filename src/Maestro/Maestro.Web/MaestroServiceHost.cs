// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights.Channel;
using System.Fabric.Health;
using System.Fabric;
using System.Net;
using System.Threading;
using System;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;

namespace Maestro.Web;

public class MaestroServiceHost : ServiceHost
{
    private MaestroServiceHost() { }

    /// <summary>
    ///     Configure and run a new ServiceHost
    /// </summary>
    public new static void Run(Action<ServiceHost> configure)
    {
        // Because of this issue, the activity tracking causes
        // arbitrarily HttpClient calls to crash, so disable it until
        // it is fixed
        // https://github.com/dotnet/runtime/issues/36908
        AppContext.SetSwitch("System.Net.Http.EnableActivityPropagation", false);
        CodePackageActivationContext packageActivationContext = FabricRuntime.GetActivationContext();
        try
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.CheckCertificateRevocationList = true;
            JsonConvert.DefaultSettings =
                () => new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None };
            var loggingServices = new ServiceCollection();
            ConfigureLoggingServices(loggingServices);
            using var loggingServiceProvider = loggingServices.BuildServiceProvider();
            try
            {
                var loggerFactory = loggingServiceProvider.GetRequiredService<ILoggerFactory>();
                using var eventListener = ServiceHostEventListener.ListenToEventSources(loggerFactory,
                    // event sources we are interested in
                    // Service Fabric sources
                    "ServiceFramework",
                    "ActorFramework",
                    // aspnet sources
                    "Microsoft.AspNetCore.Hosting",
                    "Microsoft.AspNetCore.Http.Connections",
                    "Microsoft-AspNetCore-Server-Kestrel",
                    // dotnet sources
                    "System.Data.DataCommonEventSource");
                var host = new MaestroServiceHost();
                configure(host);
                host.Start();
                packageActivationContext.ReportDeployedServicePackageHealth(
                    new HealthInformation("ServiceHost", "ServiceHost.Run", HealthState.Ok));
                Thread.Sleep(Timeout.Infinite);
            }
            finally
            {
                try
                {
                    loggingServiceProvider.GetService<ITelemetryChannel>()?.Flush();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to flush application insights telemetry channel. {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            packageActivationContext.ReportDeployedServicePackageHealth(
                new HealthInformation("ServiceHost", "ServiceHost.Run", HealthState.Error)
                {
                    Description = $"Unhandled Exception: {ex}"
                },
                new HealthReportSendOptions { Immediate = true });
            Thread.Sleep(5000);
            Environment.Exit(-1);
        }
    }

    public new ServiceHost RegisterStatelessWebService<TStartup>(string serviceTypeName, Action<IWebHostBuilder> configureWebHost = null) where TStartup : class
    {
        RegisterStatelessService(
            serviceTypeName,
            context => new MaestroDelegatedStatelessWebService<TStartup>(
                context,
                configureWebHost ?? (builder => { }),
                ApplyConfigurationToServices));
        return ConfigureServices(builder => builder.AddScoped<TStartup>());
    }
}

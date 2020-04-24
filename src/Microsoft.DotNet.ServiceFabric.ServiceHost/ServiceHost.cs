// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Health;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.Metrics;
using Microsoft.DotNet.ServiceFabric.ServiceHost.Actors;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public class HostEnvironment : IWebHostEnvironment, IHostEnvironment
    {
        public HostEnvironment(string environmentName, string applicationName, string contentRootPath, IFileProvider contentRootFileProvider)
        {
            EnvironmentName = environmentName;
            ApplicationName = applicationName;
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = contentRootFileProvider;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
        public string WebRootPath { get; set; } = null!;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
    }

    /// <summary>
    ///     A Service Fabric service host that supports activating services via dependency injection.
    /// </summary>
    public partial class ServiceHost
    {
        private readonly List<Action<IServiceCollection>> _configureServicesActions =
            new List<Action<IServiceCollection>> {ConfigureDefaultServices};

        private readonly List<Func<Task>> _serviceCallbacks = new List<Func<Task>>();

        private ServiceHost()
        {
        }

        /// <summary>
        ///     Configure and run a new ServiceHost
        /// </summary>
        public static void Run(Action<ServiceHost> configure)
        {
            CodePackageActivationContext packageActivationContext = FabricRuntime.GetActivationContext();
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.CheckCertificateRevocationList = true;
                JsonConvert.DefaultSettings =
                    () => new JsonSerializerSettings {TypeNameHandling = TypeNameHandling.None};
                var host = new ServiceHost();
                configure(host);
                host.Start();
                packageActivationContext.ReportDeployedServicePackageHealth(
                    new HealthInformation("ServiceHost", "ServiceHost.Run", HealthState.Ok));
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                packageActivationContext.ReportDeployedServicePackageHealth(
                    new HealthInformation("ServiceHost", "ServiceHost.Run", HealthState.Error)
                    {
                        Description = $"Unhandled Exception: {ex}"
                    },
                    new HealthReportSendOptions {Immediate = true});
                Thread.Sleep(5000);
                Environment.Exit(-1);
            }
        }

        public ServiceHost ConfigureServices(Action<IServiceCollection> configure)
        {
            _configureServicesActions.Add(configure);
            return this;
        }

        private void ApplyConfigurationToServices(IServiceCollection services)
        {
            foreach (Action<IServiceCollection> act in _configureServicesActions)
            {
                act(services);
            }
        }

        private void RegisterStatelessService<TService>(
            string serviceTypeName,
            Func<StatelessServiceContext, TService> ctor) where TService : StatelessService
        {
            _serviceCallbacks.Add(() => ServiceRuntime.RegisterServiceAsync(serviceTypeName, ctor));
        }

        private void RegisterStatefulService<TService>(
            string serviceTypeName,
            Func<StatefulServiceContext, TService> ctor) where TService : StatefulService
        {
            _serviceCallbacks.Add(() => ServiceRuntime.RegisterServiceAsync(serviceTypeName, ctor));
        }

        public ServiceHost RegisterStatefulService<TService>(string serviceTypeName)
            where TService : class, IServiceImplementation
        {
            RegisterStatefulService(
                serviceTypeName,
                context => new DelegatedStatefulService<TService>(
                    context,
                    ApplyConfigurationToServices));
            return ConfigureServices(c => c.AddScoped<TService, TService>());
        }

        public ServiceHost RegisterStatelessService<TService>(string serviceTypeName)
            where TService : class, IServiceImplementation
        {
            RegisterStatelessService(
                serviceTypeName,
                context => new DelegatedStatelessService<TService>(
                    context,
                    ApplyConfigurationToServices));
            return ConfigureServices(c => c.AddScoped<TService, TService>());
        }

        private void RegisterActorService<TService, TActor>(
            Func<StatefulServiceContext, ActorTypeInformation, TService> ctor)
            where TService : ActorService where TActor : Actor
        {
            _serviceCallbacks.Add(() => ActorRuntime.RegisterActorAsync<TActor>(ctor));
        }

        private void RegisterStatefulActorService<TActor>(
            string actorName,
            Func<StatefulServiceContext, ActorTypeInformation, Func<ActorService, ActorId, IServiceScopeFactory, Action<IServiceProvider>, ActorBase>, ActorService> ctor)
            where TActor : IActor, IActorImplementation
        {
            (Type actorType,
                    Func<ActorService, ActorId, IServiceScopeFactory, Action<IServiceProvider>, ActorBase> actorFactory) =
                DelegatedActor.CreateActorTypeAndFactory<TActor>(actorName);
            // ReSharper disable once PossibleNullReferenceException
            // The method search parameters are hard coded
            MethodInfo registerActorAsyncMethod = typeof(ActorRuntime).GetMethod(
                    "RegisterActorAsync",
                    new[]
                    {
                        typeof(Func<StatefulServiceContext, ActorTypeInformation, ActorService>),
                        typeof(TimeSpan),
                        typeof(CancellationToken)
                    })!
                .MakeGenericMethod(actorType);
            _serviceCallbacks.Add(
                () => (Task) registerActorAsyncMethod.Invoke(
                    null,
                    new object[]
                    {
                        (Func<StatefulServiceContext, ActorTypeInformation, ActorService>) ((context, info) =>
                            ctor(context, info, actorFactory)),
                        default(TimeSpan),
                        default(CancellationToken)
                    })!);
        }

        public ServiceHost RegisterStatefulActorService<
            [MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
            TActor>(string actorName) where TActor : class, IActor, IActorImplementation
        {
            RegisterStatefulActorService<TActor>(
                actorName,
                (context, info, actorFactory) =>
                {
                    return new DelegatedActorService<TActor>(
                        context,
                        info,
                        ApplyConfigurationToServices,
                        actorFactory);
                });
            return ConfigureServices(builder => builder.AddScoped<TActor>());
        }

        public ServiceHost RegisterStatelessWebService<TStartup>(string serviceTypeName, Action<IWebHostBuilder> configureWebHost = null) where TStartup : class
        {
            RegisterStatelessService(
                serviceTypeName,
                context => new DelegatedStatelessWebService<TStartup>(
                    context,
                    configureWebHost ?? (builder => { }),
                    ApplyConfigurationToServices));
            return ConfigureServices(builder => builder.AddScoped<TStartup>());
        }

        private void Start()
        {
            foreach (Func<Task> svc in _serviceCallbacks)
            {
                svc().GetAwaiter().GetResult();
            }
        }

        public static void ConfigureDefaultServices(IServiceCollection services)
        {
            services.AddOptions();
            services.SetupConfiguration();
            services.TryAddSingleton(InitializeEnvironment());
            services.TryAddSingleton(b => (IHostEnvironment) b.GetService<HostEnvironment>());
            services.TryAddSingleton(b => (IWebHostEnvironment) b.GetService<HostEnvironment>());
            ConfigureApplicationInsights(services);
            services.AddLogging(
                builder =>
                {
                    builder.AddDebug();
                    builder.AddFixedApplicationInsights(LogLevel.Information);
                });
            services.TryAddSingleton<IMetricTracker, ApplicationInsightsMetricTracker>();
            services.TryAddSingleton(typeof(IActorProxyFactory<>), typeof(ActorProxyFactory<>));
        }

        public static HostEnvironment InitializeEnvironment()
        {
            string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                              Environment.GetEnvironmentVariable("ENVIRONMENT") ??
                              throw new InvalidOperationException("Could Not find environment.");
            string contentRoot = AppContext.BaseDirectory;
            var contentRootFileProvider = new PhysicalFileProvider(contentRoot);
            return new HostEnvironment(environment, GetApplicationName(), contentRoot, contentRootFileProvider);
        }

        private static string GetApplicationName()
        {
            return Environment.GetEnvironmentVariable("Fabric_ApplicationName") ?? Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown";
        }
    }
}

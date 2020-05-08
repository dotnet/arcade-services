// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Fabric;
using System.Linq;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Client;
using Microsoft.ServiceFabric.Actors.Remoting.V2.FabricTransport.Client;
using Microsoft.ServiceFabric.Services.Client;
using Microsoft.ServiceFabric.Services.Communication.Client;
using Microsoft.ServiceFabric.Services.Remoting;
using Microsoft.ServiceFabric.Services.Remoting.Client;
using Microsoft.ServiceFabric.Services.Remoting.FabricTransport;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.V2.FabricTransport.Client;
using Microsoft.ServiceFabric.Services.Remoting.V2.FabricTransport.Runtime;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public static class ServiceHostRemoting
    {
        internal static IServiceRemotingListener CreateServiceRemotingListener<TImplementation>(
            ServiceContext context,
            Type[] ifaces,
            IServiceProvider container)
        {
            var client = container.GetRequiredService<TelemetryClient>();
            Type firstIface = ifaces[0];
            Type[] additionalIfaces = ifaces.Skip(1).ToArray();
            var gen = new ProxyGenerator();
            var impl = (IService) gen.CreateInterfaceProxyWithTargetInterface(
                firstIface,
                additionalIfaces,
                (object) null,
                new InvokeInNewScopeInterceptor<TImplementation>(container),
                new LoggingServiceInterceptor(context, client));

            return new FabricTransportServiceRemotingListener(
                context,
                new ActivityServiceRemotingMessageDispatcher(context, impl, null));
        }
    }

    public static class ServiceHostActorProxy
    {
        private static ProxyGenerator Generator { get; } = new ProxyGenerator();

        private static ActorProxyFactory CreateFactory()
        {
            return new ActorProxyFactory(
                handler => new ActivityServiceRemotingClientFactory(
                    new FabricTransportActorRemotingClientFactory(handler)));
        }

        public static T Create<T>(ActorId actorId, TelemetryClient telemetryClient, ServiceContext context)
            where T : class, IActor
        {
            var actor = CreateFactory().CreateActorProxy<T>(actorId);
            Uri serviceUri = actor.GetActorReference().ServiceUri;
            T proxy = Generator.CreateInterfaceProxyWithTargetInterface(
                actor,
                new LoggingServiceProxyInterceptor(telemetryClient, context, serviceUri.ToString()));
            return proxy;
        }
    }

    public static class ServiceHostProxy
    {
        private static ProxyGenerator Generator { get; } = new ProxyGenerator();

        private static ServiceProxyFactory CreateFactory()
        {
            if (!FabricTransportRemotingSettings.TryLoadFrom(
                "TransportSettings",
                out FabricTransportRemotingSettings transportSettings))
            {
                transportSettings = new FabricTransportRemotingSettings();
            }

            return new ServiceProxyFactory(
                handler => new ActivityServiceRemotingClientFactory(
                    new FabricTransportServiceRemotingClientFactory(transportSettings, handler)));
        }

        public static T Create<T>(
            Uri serviceUri,
            TelemetryClient telemetryClient,
            ServiceContext context,
            ServicePartitionKey partitionKey = null,
            TargetReplicaSelector targetReplicaSelector = TargetReplicaSelector.Default) where T : class, IService
        {
            T service = CreateFactory().CreateServiceProxy<T>(serviceUri, partitionKey, targetReplicaSelector);
            T proxy = Generator.CreateInterfaceProxyWithTargetInterface(
                service,
                new LoggingServiceProxyInterceptor(telemetryClient, context, serviceUri.ToString()));
            return proxy;
        }
    }

    internal class InvokeInNewScopeInterceptor<TService> : AsyncInterceptor
    {
        private readonly Action<TService> _configureScope;
        private readonly IServiceProvider _outerScope;

        public InvokeInNewScopeInterceptor(IServiceProvider outerScope) : this(
            outerScope,
            builder => { })
        {
        }

        public InvokeInNewScopeInterceptor(
            IServiceProvider outerScope,
            Action<TService> configureScope)
        {
            _outerScope = outerScope;
            _configureScope = configureScope;
        }

        protected override async Task<T> InterceptAsync<T>(IInvocation invocation, Func<Task<T>> call)
        {
            using (IServiceScope scope = _outerScope.CreateScope())
            {
                var client = scope.ServiceProvider.GetRequiredService<TelemetryClient>();
                var context = scope.ServiceProvider.GetRequiredService<ServiceContext>();
                string url =
                    $"{context.ServiceName}/{invocation.Method?.DeclaringType?.Name}/{invocation.Method?.Name}";
                using (IOperationHolder<RequestTelemetry> op = client.StartOperation<RequestTelemetry>($"RPC {url}"))
                {
                    try
                    {
                        op.Telemetry.Url = new Uri(url);
                        
                        var instance = scope.ServiceProvider.GetRequiredService<TService>();
                        _configureScope(instance);
                        ((IChangeProxyTarget) invocation).ChangeInvocationTarget(instance);
                        return await call();
                    }
                    catch (Exception ex)
                    {
                        op.Telemetry.Success = false;
                        client.TrackException(ex);
                        throw;
                    }
                }
            }
        }
    }
}

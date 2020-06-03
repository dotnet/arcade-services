using System;
using System.Fabric;
using Castle.DynamicProxy;
using Microsoft.ApplicationInsights;
using Microsoft.ServiceFabric.Services.Client;
using Microsoft.ServiceFabric.Services.Communication.Client;
using Microsoft.ServiceFabric.Services.Remoting;
using Microsoft.ServiceFabric.Services.Remoting.Client;
using Microsoft.ServiceFabric.Services.Remoting.FabricTransport;
using Microsoft.ServiceFabric.Services.Remoting.V2.FabricTransport.Client;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
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
}

using System;
using System.Fabric;
using Castle.DynamicProxy;
using Microsoft.ApplicationInsights;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Client;
using Microsoft.ServiceFabric.Actors.Remoting.V2.FabricTransport.Client;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
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
}

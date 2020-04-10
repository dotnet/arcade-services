using System.Fabric;
using Microsoft.ApplicationInsights;
using Microsoft.ServiceFabric.Actors;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public interface IActorProxyFactory<out TActor>
    {
        TActor Lookup(ActorId id);
    }

    public class ActorProxyFactory<TActor> : IActorProxyFactory<TActor> where TActor : class, IActor
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly ServiceContext _serviceContext;

        public ActorProxyFactory(TelemetryClient telemetryClient = null, ServiceContext serviceContext = null)
        {
            _telemetryClient = telemetryClient;
            _serviceContext = serviceContext;
        }

        public TActor Lookup(ActorId id)
        {
            return ServiceHostActorProxy.Create<TActor>(id, _telemetryClient, _serviceContext);
        }
    }
}

using System.Fabric;
using Microsoft.ApplicationInsights;
using Microsoft.ServiceFabric.Actors;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public interface IActorLookup<out TActor>
    {
        TActor Lookup(ActorId id);
    }

    public class ActorLookup<TActor> : IActorLookup<TActor> where TActor : class, IActor
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly ServiceContext _serviceContext;

        public ActorLookup(TelemetryClient telemetryClient = null, ServiceContext serviceContext = null)
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

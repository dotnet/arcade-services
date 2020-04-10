using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Actors
{
    public interface IActorImplementation
    {
        void Initialize(ActorId actorId, IActorStateManager stateManager, IReminderManager reminderManager);
    }
}

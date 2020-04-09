using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Actors
{
    public interface IStatefulActor
    {
        void InitializeActorState(ActorId actorId, IActorStateManager stateManager, IReminderManager reminderManager);
    }
}

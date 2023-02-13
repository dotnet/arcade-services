using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Actors;

public interface IActorImplementation
{
    void Initialize(ActorId actorId, IActorStateManager stateManager, IReminderManager reminderManager);
    static abstract Task RegisterActorAsync(Action<IServiceCollection> configureServices);
}

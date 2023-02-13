// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Services.Communication.Runtime;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Actors;

public interface IReminderManager
{
    Task<IActorReminder> TryRegisterReminderAsync(
        string reminderName,
        byte[] state,
        TimeSpan dueTime,
        TimeSpan period);

    Task TryUnregisterReminderAsync(string reminderName);
}

public abstract class DelegatedActor : Actor, IReminderManager
{
    public DelegatedActor(ActorService actorService, ActorId actorId) : base(actorService, actorId)
    {
    }

    public Task<IActorReminder> TryRegisterReminderAsync(
        string reminderName,
        byte[] state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        try
        {
            return Task.FromResult(GetReminder(reminderName));
        }
        catch (ReminderNotFoundException)
        {
            return RegisterReminderAsync(reminderName, state, dueTime, period);
        }
    }

    public async Task TryUnregisterReminderAsync(string reminderName)
    {
        try
        {
            IActorReminder reminder = GetReminder(reminderName);
            await UnregisterReminderAsync(reminder);
        }
        catch (ReminderNotFoundException)
        {
        }
    }
}

public class DelegatedActorService<TActorImplementation> : ActorService
{
    private readonly Func<ActorService, ActorId, IServiceScopeFactory, ActorBase> _actorFactory;
    private readonly Action<IServiceCollection> _configureServices;

    public DelegatedActorService(
        StatefulServiceContext context,
        ActorTypeInformation actorTypeInfo,
        Action<IServiceCollection> configureServices,
        Func<ActorService, ActorId, IServiceScopeFactory, ActorBase> actorFactory,
        ActorServiceSettings settings = null) : base(
        context,
        actorTypeInfo,
        ActorFactory,
        null,
        new KvsActorStateProvider(),
        settings)
    {
        _configureServices = configureServices;
        _actorFactory = actorFactory;
    }

    private ServiceProvider Container { get; set; }

    protected override async Task OnOpenAsync(ReplicaOpenMode openMode, CancellationToken cancellationToken)
    {
        await base.OnOpenAsync(openMode, cancellationToken);

        var services = new ServiceCollection();
        services.AddSingleton<ServiceContext>(Context);
        services.AddSingleton(Context);
        _configureServices(services);
        Container = services.BuildServiceProvider();

        // This requires the ServiceContext up a few lines, so we can't inject it in the constructor
        Container.GetService<TemporaryFiles>()?.Initialize();
    }

    protected override async Task OnCloseAsync(CancellationToken cancellationToken)
    {
        await base.OnCloseAsync(cancellationToken);
        Container?.Dispose();
    }

    protected override void OnAbort()
    {
        base.OnAbort();
        Container?.Dispose();
    }

    protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
    {
        return base.CreateServiceReplicaListeners();
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        await Lifecycle.OnStartingAsync(Container);
        var logger = Container.GetRequiredService<ILogger<DelegatedActor>>();
        try
        {
            await using var _ =
                cancellationToken.Register(() => logger.LogInformation("Service abort cancellation requested"));
            logger.LogInformation("Entering service 'RunAsync'");
            await base.RunAsync(cancellationToken);
            logger.LogWarning("Abnormal service exit without cancellation");
        }
        catch (OperationCanceledException e) when (e.CancellationToken == cancellationToken)
        {
            logger.LogInformation("Service shutdown complete");
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Unhandled exception crashing actor execution");
            throw;
        }
        finally
        {
            await Lifecycle.OnStoppingAsync(Container);
        }
    }

    private ActorBase CreateActor(ActorId actorId)
    {
        return _actorFactory(this, actorId, Container.GetService<IServiceScopeFactory>() ?? throw new InvalidOperationException("Actor created before OnOpenAsync"));
    }

    private static ActorBase ActorFactory(ActorService service, ActorId actorId)
    {
        return ((DelegatedActorService<TActorImplementation>) service).CreateActor(actorId);
    }
}

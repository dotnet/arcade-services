// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Castle.DynamicProxy.Generators;
using Castle.DynamicProxy.Internal;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Services.Communication.Runtime;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Actors
{
    public interface IReminderManager
    {
        Task<IActorReminder> TryRegisterReminderAsync(
            string reminderName,
            byte[] state,
            TimeSpan dueTime,
            TimeSpan period);

        Task TryUnregisterReminderAsync(string reminderName);
    }

    public class DelegatedActor : Actor, IReminderManager, IRemindable
    {
        public DelegatedActor(ActorService actorService, ActorId actorId) : base(actorService, actorId)
        {
        }

        public static ChangeNameProxyGenerator Generator { get; } = new ChangeNameProxyGenerator();

        public Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            throw new InvalidOperationException("This method call should always be intercepted.");
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

        public static (Type, Func<ActorService, ActorId, IServiceScopeFactory, Action<IServiceProvider>, ActorBase>)
            CreateActorTypeAndFactory<TActor>(string actorName) where TActor : IActorImplementation
        {
            Type type = Generator.CreateClassProxyType(
                actorName,
                typeof(DelegatedActor),
                typeof(TActor).GetAllInterfaces()
                    .Where(i => typeof(IActor).IsAssignableFrom(i) || i == typeof(IRemindable))
                    .ToArray(),
                ProxyGenerationOptions.Default);

            ActorBase Factory(
                ActorService service,
                ActorId id,
                IServiceScopeFactory outerScope,
                Action<IServiceProvider> configueScope)
            {
                var args = new object[] {service, id};
                return (ActorBase) Generator.CreateProxyFromProxyType(
                    type,
                    ProxyGenerationOptions.Default,
                    args,
                    new ActorMethodInterceptor<TActor>(outerScope));
            }

            return (type, Factory);
        }
    }

    internal class ActorMethodInterceptor<TActor> : AsyncInterceptor where TActor : IActorImplementation
    {
        private readonly IServiceScopeFactory _outerScope;

        public ActorMethodInterceptor(IServiceScopeFactory outerScope)
        {
            _outerScope = outerScope;
        }

        protected override void Proceed(IInvocation invocation)
        {
            MethodInfo method = invocation.Method;
            invocation.ReturnValue = method.Invoke(invocation.ReturnValue, invocation.Arguments);
        }

        private bool ShouldIntercept(IInvocation invocation)
        {
            return (invocation.Method.DeclaringType?.IsInterface ?? false) &&
                   invocation.Method.DeclaringType != typeof(IReminderManager);
        }

        public override void Intercept(IInvocation invocation)
        {
            if (!ShouldIntercept(invocation))
            {
                invocation.Proceed();
                return;
            }

            base.Intercept(invocation);
        }

        protected override async Task<T> InterceptAsync<T>(IInvocation invocation, Func<Task<T>> call)
        {
            var actor = (Actor) invocation.Proxy;
            using (IServiceScope scope = _outerScope.CreateScope())
            {
                var client = scope.ServiceProvider.GetRequiredService<TelemetryClient>();
                var context = scope.ServiceProvider.GetRequiredService<ServiceContext>();
                ActorId id = actor.Id;
                string url =
                    $"{context.ServiceName}/{id}/{invocation.Method?.DeclaringType?.Name}/{invocation.Method?.Name}";
                using (IOperationHolder<RequestTelemetry> op = client.StartOperation<RequestTelemetry>($"RPC {url}"))
                {
                    try
                    {
                        op.Telemetry.Url = new Uri(url);
                        
                        TActor a = scope.ServiceProvider.GetRequiredService<TActor>();
                        a.Initialize(actor.Id, actor.StateManager, actor as IReminderManager);
                        invocation.ReturnValue = a;
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

    public class ChangeNameProxyGenerator : ProxyGenerator
    {
        public ChangeNameProxyGenerator() : base(new CustomProxyBuilder())
        {
        }

        public Type CreateClassProxyType(
            string name,
            Type classToProxy,
            Type[] additionalInterfacesToProxy,
            ProxyGenerationOptions options)
        {
            CustomNamingScope.SuggestedName = "Castle.Proxies." + classToProxy.Name + "Proxy";
            CustomNamingScope.CurrentName = name;
            return CreateClassProxyType(classToProxy, additionalInterfacesToProxy, options);
        }

        public object CreateProxyFromProxyType(
            Type proxyType,
            ProxyGenerationOptions options,
            object[] constructorArguments,
            params IInterceptor[] interceptors)
        {
            List<object> proxyArguments = BuildArgumentListForClassProxy(options, interceptors);
            if (constructorArguments != null && constructorArguments.Length != 0)
            {
                proxyArguments.AddRange(constructorArguments);
            }

            return CreateClassProxyInstance(proxyType, proxyArguments, proxyType, constructorArguments);
        }

        private class CustomProxyBuilder : DefaultProxyBuilder
        {
            public CustomProxyBuilder() : base(new CustomModuleScope())
            {
            }
        }

        private class CustomModuleScope : ModuleScope
        {
            public CustomModuleScope() : base(
                false,
                false,
                new CustomNamingScope(),
                DEFAULT_ASSEMBLY_NAME,
                DEFAULT_FILE_NAME,
                DEFAULT_ASSEMBLY_NAME,
                DEFAULT_FILE_NAME)
            {
            }
        }

        private class CustomNamingScope : NamingScope, INamingScope
        {
            public static volatile string SuggestedName;
            public static volatile string CurrentName;

            string INamingScope.GetUniqueName(string suggestedName)
            {
                if (suggestedName == SuggestedName)
                {
                    return CurrentName;
                }

                return base.GetUniqueName(suggestedName);
            }
        }
    }

    public class DelegatedActorService<TActorImplementation> : ActorService
    {
        private readonly Func<ActorService, ActorId, IServiceScopeFactory, Action<IServiceProvider>, ActorBase> _actorFactory;
        private readonly Action<IServiceCollection> _configureServices;

        public DelegatedActorService(
            StatefulServiceContext context,
            ActorTypeInformation actorTypeInfo,
            Action<IServiceCollection> configureServices,
            Func<ActorService, ActorId, IServiceScopeFactory, Action<IServiceProvider>, ActorBase> actorFactory,
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
        }

        private ActorBase CreateActor(ActorId actorId)
        {
            return _actorFactory(this, actorId, Container.GetService<IServiceScopeFactory>() ?? throw new InvalidOperationException("Actor created before OnOpenAsync"), builder => { });
        }

        private static ActorBase ActorFactory(ActorService service, ActorId actorId)
        {
            return ((DelegatedActorService<TActorImplementation>) service).CreateActor(actorId);
        }
    }
}

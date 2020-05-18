using System;
using System.Fabric;
using Castle.DynamicProxy;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    public class InvokeInNewScopeInterceptorTests
    {
        [Fact]
        public void InterceptCreatesScope()
        {
            var telemetryChannel = new FakeChannel();
            var config = new TelemetryConfiguration("00000000-0000-0000-0000-000000000001", telemetryChannel);
            var client = new TelemetryClient(config);

            var ctx = new Mock<ServiceContext>(
                new NodeContext("IGNORED", new NodeId(1, 1), 1, "IGNORED", "IGNORED.test"),
                Mock.Of<ICodePackageActivationContext>(),
                "TestService",
                new Uri("service://TestName"),
                new byte[0],
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                1);

            Thing innerThing = null;

            var collection = new ServiceCollection();
            collection.AddSingleton(client);
            collection.AddScoped<IFakeService, FakeService>();
            collection.AddSingleton(ctx.Object);
            collection.AddScoped<Thing>();
            collection.AddSingleton<Action<Thing>>(t => innerThing = t);
            ServiceProvider provider = collection.BuildServiceProvider();

            var outerThing = provider.GetRequiredService<Thing>();

            var gen = new ProxyGenerator();
            var impl = (IFakeService) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IFakeService),
                new Type[0],
                (object) null,
                new InvokeInNewScopeInterceptor<IFakeService>(provider));

            Assert.Equal(FakeService.RetValue, impl.Test());
            Assert.Equal(1, FakeService.Calls);
            Assert.NotNull(innerThing);

            // It's supposed to be a new scope, so it should have gotten a different thing
            Assert.NotEqual(outerThing, innerThing);

            client.Flush();
            Assert.Equal(1, telemetryChannel.HitCount);
            Assert.NotNull(telemetryChannel.RequestName);
            Assert.Contains("IFakeService", telemetryChannel.RequestName, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("service://TestName", telemetryChannel.RequestName, StringComparison.OrdinalIgnoreCase);
        }

        
        private class Thing
        {
            private static int s_instanceId = 1;
            private readonly string _name = $"Thing Instance {s_instanceId++}";
            public override string ToString() => _name;
        }

        // ReSharper disable once MemberCanBePrivate.Global This is Mocked, so much be public
        public class FakeChannel : ITelemetryChannel
        {
            public void Dispose()
            {
            }

            public void Send(ITelemetry item)
            {
                if (item is EventTelemetry ev && ev.Name == "TestEvent")
                {
                    HitCount++;
                }

                if (item is RequestTelemetry req)
                {
                    RequestName = req.Name;
                }
            }

            public void Flush()
            {
            }

            public bool? DeveloperMode { get; set; }
            public string EndpointAddress { get; set; }

            public int HitCount { get; private set; }
            public string RequestName { get; private set; }
        }
        
        // ReSharper disable once MemberCanBePrivate.Global This is Proxied, so much be public
        public interface IFakeService
        {
            string Test();
        }

        private class FakeService : IFakeService
        {
            public const string RetValue = "TestReturnValue";

            private readonly TelemetryClient _client;
            private readonly Thing _thing;
            private readonly Action<Thing> _action;

            public FakeService(TelemetryClient client, Thing thing, Action<Thing> action)
            {
                _client = client;
                _thing = thing;
                _action = action;
            }

            public string Test()
            {
                _action(_thing);
                Calls++;
                _client.TrackEvent("TestEvent");
                return RetValue;
            }

            public static int Calls { get; private set; }
        }
    }
}

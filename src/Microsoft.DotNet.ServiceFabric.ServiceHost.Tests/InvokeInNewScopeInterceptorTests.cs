using System;
using System.Fabric;
using System.Reflection;
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
        private class Thing
        {
            private static int s_instanceId = 1;
            private readonly string _name = $"Thing Instance {s_instanceId++}";

            public override string ToString()
            {
                return _name;
            }
        }

        // ReSharper disable once MemberCanBePrivate.Global This is Mocked, so much be public
        public class FakeChannel : ITelemetryChannel
        {
            public int HitCount { get; private set; }
            public string RequestName { get; private set; }
            public bool RequestSuccess { get; private set; }

            public Exception Exception { get; private set; }

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
                    RequestSuccess = req.Success ?? true;
                }

                if (item is ExceptionTelemetry ex)
                {
                    Exception = ex.Exception;
                }
            }

            public void Flush()
            {
            }

            public bool? DeveloperMode { get; set; }
            public string EndpointAddress { get; set; }
        }

        // ReSharper disable once MemberCanBePrivate.Global This is Proxied, so much be public
        public interface IFakeService
        {
            string Test();
        }

        private class FakeService : IFakeService
        {
            public const string RetValue = "TestReturnValue";
            private readonly Action<Thing> _action;

            private readonly TelemetryClient _client;
            private readonly Thing _thing;

            public FakeService(TelemetryClient client, Thing thing, Action<Thing> action)
            {
                _client = client;
                _thing = thing;
                _action = action;
            }

            public static int Calls { get; private set; }

            public string Test()
            {
                _action(_thing);
                Calls++;
                _client.TrackEvent("TestEvent");
                return RetValue;
            }
        }

        [Fact]
        public void InterceptCatchesExceptions()
        {
            var telemetryChannel = new FakeChannel();
            var config = new TelemetryConfiguration("00000000-0000-0000-0000-000000000001", telemetryChannel);
            var client = new TelemetryClient(config);

            Mock<ServiceContext> ctx = MockBuilder.MockServiceContext();

            var service = new Mock<IFakeService>();
            service.Setup(s => s.Test()).Throws(new InvalidOperationException("Test Exception Text"));

            var collection = new ServiceCollection();
            collection.AddSingleton(client);
            collection.AddSingleton(ctx.Object);
            collection.AddSingleton(service.Object);
            ServiceProvider provider = collection.BuildServiceProvider();

            var gen = new ProxyGenerator();
            var impl = (IFakeService) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IFakeService),
                new Type[0],
                (object) null,
                new InvokeInNewScopeInterceptor<IFakeService>(provider));

            var ex = Assert.ThrowsAny<Exception>(() => impl.Test());
            Assert.IsAssignableFrom<TargetInvocationException>(ex);
            Assert.NotNull(ex.InnerException);
            Assert.IsAssignableFrom<InvalidOperationException>(ex.InnerException);
            Assert.Equal("Test Exception Text", ex.InnerException.Message);

            client.Flush();

            Assert.False(telemetryChannel.RequestSuccess);
            Assert.Same(ex.InnerException, telemetryChannel.Exception);
        }

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
            Assert.True(telemetryChannel.RequestSuccess);
            Assert.Contains("IFakeService", telemetryChannel.RequestName, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("service://TestName", telemetryChannel.RequestName, StringComparison.OrdinalIgnoreCase);
        }
    }
}

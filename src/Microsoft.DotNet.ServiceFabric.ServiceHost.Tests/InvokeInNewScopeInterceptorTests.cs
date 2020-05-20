using System;
using System.Fabric;
using System.Linq;
using System.Reflection;
using Castle.DynamicProxy;
using Microsoft.ApplicationInsights;
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

            public string TestServiceMethod()
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
            service.Setup(s => s.TestServiceMethod()).Throws(new InvalidOperationException("Test Exception Text"));

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

            var ex = Assert.Throws<InvalidOperationException>(() => impl.TestServiceMethod());
            Assert.Equal("Test Exception Text", ex.Message);

            client.Flush();

            RequestTelemetry requestTelemetry = telemetryChannel.Telemetry.OfType<RequestTelemetry>().FirstOrDefault();
            Assert.NotNull(requestTelemetry);
            Assert.False(requestTelemetry.Success);
            ExceptionTelemetry exceptionTelemetry = telemetryChannel.Telemetry.OfType<ExceptionTelemetry>().FirstOrDefault();
            Assert.NotNull(exceptionTelemetry);
            Assert.Same(ex, exceptionTelemetry.Exception);
        }

        [Fact]
        public void InterceptCreatesScope()
        {
            var telemetryChannel = new FakeChannel();
            var config = new TelemetryConfiguration("00000000-0000-0000-0000-000000000001", telemetryChannel);
            var client = new TelemetryClient(config);
            
            Mock<ServiceContext> ctx = MockBuilder.MockServiceContext();

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

            Assert.Equal(FakeService.RetValue, impl.TestServiceMethod());
            Assert.Equal(1, FakeService.Calls);
            Assert.NotNull(innerThing);

            // It's supposed to be a new scope, so it should have gotten a different thing
            Assert.NotEqual(outerThing, innerThing);

            client.Flush();
            Assert.Equal(1, telemetryChannel.Telemetry.OfType<EventTelemetry>().Count(e => e.Name == "TestEvent"));
            RequestTelemetry requestTelemetry = telemetryChannel.Telemetry.OfType<RequestTelemetry>().FirstOrDefault();
            Assert.NotNull(requestTelemetry);
            Assert.NotNull(requestTelemetry.Name);
            Assert.True(requestTelemetry.Success ?? true);
            Assert.Contains("IFakeService", requestTelemetry.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("service://TestName", requestTelemetry.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ReSharper disable once MemberCanBePrivate.Global This is Proxied, so much be public
}

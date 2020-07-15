using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using Castle.DynamicProxy;
using FluentAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    [TestFixture]
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

        // ReSharper disable once MemberCanBePrivate.Global This is Mocked, so must be public

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

        [Test]
        public void InterceptCatchesExceptions()
        {
            var telemetryChannel = new FakeChannel();
            var config = new TelemetryConfiguration("00000000-0000-0000-0000-000000000001", telemetryChannel);
            var client = new TelemetryClient(config);


            var service = new Mock<IFakeService>();
            service.Setup(s => s.TestServiceMethod()).Throws(new InvalidOperationException("Test Exception Text"));

            var collection = new ServiceCollection();
            collection.AddSingleton(client);
            MockBuilder.RegisterStatelessServiceContext(collection);
            collection.AddSingleton(service.Object);
            ServiceProvider provider = collection.BuildServiceProvider();

            var gen = new ProxyGenerator();
            var impl = (IFakeService) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IFakeService),
                new Type[0],
                (object) null,
                new InvokeInNewScopeInterceptor<IFakeService>(provider));

            var ex = (((Func<object>)(() => impl.TestServiceMethod()))).Should().Throw<InvalidOperationException>().Which;
            ex.Message.Should().Be("Test Exception Text");

            client.Flush();

            RequestTelemetry requestTelemetry = telemetryChannel.Telemetry.OfType<RequestTelemetry>().FirstOrDefault();
            requestTelemetry.Should().NotBeNull();
            requestTelemetry.Success.Should().BeFalse();
            ExceptionTelemetry exceptionTelemetry = telemetryChannel.Telemetry.OfType<ExceptionTelemetry>().FirstOrDefault();
            exceptionTelemetry.Should().NotBeNull();
            exceptionTelemetry.Exception.Should().BeSameAs(ex);
        }

        [Test]
        public void InterceptCreatesScope()
        {
            var telemetryChannel = new FakeChannel();
            var config = new TelemetryConfiguration("00000000-0000-0000-0000-000000000001", telemetryChannel);
            var client = new TelemetryClient(config);
            

            Thing innerThing = null;

            var collection = new ServiceCollection();
            collection.AddSingleton(client);
            collection.AddScoped<IFakeService, FakeService>();
            MockBuilder.RegisterStatelessServiceContext(collection);
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

            impl.TestServiceMethod().Should().Be(FakeService.RetValue);
            FakeService.Calls.Should().Be(1);
            innerThing.Should().NotBeNull();

            // It's supposed to be a new scope, so it should have gotten a different thing
            innerThing.Should().NotBe(outerThing);

            client.Flush();
            telemetryChannel.Telemetry.OfType<EventTelemetry>().Count(e => e.Name == "TestEvent").Should().Be(1);
            List<RequestTelemetry> requestTelemetries =
                telemetryChannel.Telemetry.OfType<RequestTelemetry>().ToList();
            requestTelemetries.Should().ContainSingle();
            RequestTelemetry requestTelemetry = requestTelemetries[0];
            requestTelemetry.Name.Should().NotBeNull();
            (requestTelemetry.Success ?? true).Should().BeTrue();
            requestTelemetry.Name.Should().Contain("IFakeService");
            requestTelemetry.Name.Should().ContainEquivalentOf("service://TestName");
            
            telemetryChannel.Telemetry.OfType<ExceptionTelemetry>().Should().BeEmpty();
        }
    }
}

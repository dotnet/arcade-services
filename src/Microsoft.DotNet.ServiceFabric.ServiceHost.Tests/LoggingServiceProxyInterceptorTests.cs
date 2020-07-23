using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Reflection;
using Castle.DynamicProxy;
using FluentAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    [TestFixture]
    public class LoggingServiceProxyInterceptorTests
    {
        [Test]
        public void AggregateExceptionIsUnwrapped()
        {
            var telemetryChannel = new FakeChannel();
            var config = new TelemetryConfiguration("00000000-0000-0000-0000-000000000001", telemetryChannel);
            var client = new TelemetryClient(config);

            StatelessServiceContext ctx = MockBuilder.StatelessServiceContext();

            var service = new Mock<IFakeService>();
            service.Setup(s => s.TestServiceMethod())
                .Throws(new AggregateException(new InvalidOperationException("Test exception text")));

            var gen = new ProxyGenerator();
            var impl = (IFakeService) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IFakeService),
                new Type[0],
                service.Object,
                new LoggingServiceProxyInterceptor(client, ctx, "other://uri.test"));

            var invocationException = (((Func<object>)(() => impl.TestServiceMethod()))).Should().Throw<InvalidOperationException>().Which;
            client.Flush();
            List<DependencyTelemetry> dependencyTelemetries =
                telemetryChannel.Telemetry.OfType<DependencyTelemetry>().ToList();
            dependencyTelemetries.Should().ContainSingle();
            DependencyTelemetry dependencyTelemetry = dependencyTelemetries[0];
            dependencyTelemetry.Success.Should().BeFalse();
            dependencyTelemetry.Type.Should().Be("ServiceFabricRemoting");
            dependencyTelemetry.Target.Should().Be("other://uri.test");
            dependencyTelemetry.Data.Should().StartWith("other://uri.test");
            dependencyTelemetry.Data.Should().Contain(nameof(IFakeService));
            dependencyTelemetry.Data.Should().Contain(nameof(IFakeService.TestServiceMethod));

            List<ExceptionTelemetry> exceptionTelemetries =
                telemetryChannel.Telemetry.OfType<ExceptionTelemetry>().ToList();
            exceptionTelemetries.Should().ContainSingle();
            ExceptionTelemetry exceptionTelemetry = exceptionTelemetries[0];
            exceptionTelemetry.Exception.Should().BeSameAs(invocationException);
        }

        [Test]
        public void ComplexAggregateExceptionIsReported()
        {
            var telemetryChannel = new FakeChannel();
            var config = new TelemetryConfiguration("00000000-0000-0000-0000-000000000001", telemetryChannel);
            var client = new TelemetryClient(config);

            StatelessServiceContext ctx = MockBuilder.StatelessServiceContext();

            var service = new Mock<IFakeService>();
            service.Setup(s => s.TestServiceMethod())
                .Throws(new AggregateException(new InvalidOperationException("Test exception text"),
                    new InvalidOperationException("Another test exception text")));

            var gen = new ProxyGenerator();
            var impl = (IFakeService) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IFakeService),
                new Type[0],
                service.Object,
                new LoggingServiceProxyInterceptor(client, ctx, "other://uri.test"));

            var invocationException = (((Func<object>)(() => impl.TestServiceMethod()))).Should().Throw<AggregateException>().Which;
            client.Flush();
            List<DependencyTelemetry> dependencyTelemetries =
                telemetryChannel.Telemetry.OfType<DependencyTelemetry>().ToList();
            dependencyTelemetries.Should().ContainSingle();
            DependencyTelemetry dependencyTelemetry = dependencyTelemetries[0];
            dependencyTelemetry.Success.Should().BeFalse();
            dependencyTelemetry.Type.Should().Be("ServiceFabricRemoting");
            dependencyTelemetry.Target.Should().Be("other://uri.test");
            dependencyTelemetry.Data.Should().StartWith("other://uri.test");
            dependencyTelemetry.Data.Should().Contain(nameof(IFakeService));
            dependencyTelemetry.Data.Should().Contain(nameof(IFakeService.TestServiceMethod));

            List<ExceptionTelemetry> exceptionTelemetries =
                telemetryChannel.Telemetry.OfType<ExceptionTelemetry>().ToList();
            exceptionTelemetries.Should().ContainSingle();
            ExceptionTelemetry exceptionTelemetry = exceptionTelemetries[0];
            exceptionTelemetry.Exception.Should().BeSameAs(invocationException);
        }

        [Test]
        public void NormalExceptionIsReported()
        {
            var telemetryChannel = new FakeChannel();
            var config = new TelemetryConfiguration("00000000-0000-0000-0000-000000000001", telemetryChannel);
            var client = new TelemetryClient(config);

            StatelessServiceContext ctx = MockBuilder.StatelessServiceContext();

            var service = new Mock<IFakeService>();
            service.Setup(s => s.TestServiceMethod()).Throws(new InvalidOperationException("Test exception text"));

            var gen = new ProxyGenerator();
            var impl = (IFakeService) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IFakeService),
                new Type[0],
                service.Object,
                new LoggingServiceProxyInterceptor(client, ctx, "other://uri.test"));

            var invocationException = (((Func<object>)(() => impl.TestServiceMethod()))).Should().Throw<InvalidOperationException>().Which;
            client.Flush();
            List<DependencyTelemetry> dependencyTelemetries =
                telemetryChannel.Telemetry.OfType<DependencyTelemetry>().ToList();
            dependencyTelemetries.Should().ContainSingle();
            DependencyTelemetry dependencyTelemetry = dependencyTelemetries[0];
            dependencyTelemetry.Success.Should().BeFalse();
            dependencyTelemetry.Type.Should().Be("ServiceFabricRemoting");
            dependencyTelemetry.Target.Should().Be("other://uri.test");
            dependencyTelemetry.Data.Should().StartWith("other://uri.test");
            dependencyTelemetry.Data.Should().Contain(nameof(IFakeService));
            dependencyTelemetry.Data.Should().Contain(nameof(IFakeService.TestServiceMethod));

            List<ExceptionTelemetry> exceptionTelemetries =
                telemetryChannel.Telemetry.OfType<ExceptionTelemetry>().ToList();
            exceptionTelemetries.Should().ContainSingle();
            ExceptionTelemetry exceptionTelemetry = exceptionTelemetries[0];
            exceptionTelemetry.Exception.Should().BeSameAs(invocationException);
        }

        [Test]
        public void SetsDependencyData()
        {
            var telemetryChannel = new FakeChannel();
            var config = new TelemetryConfiguration("00000000-0000-0000-0000-000000000001", telemetryChannel);
            var client = new TelemetryClient(config);

            StatelessServiceContext ctx = MockBuilder.StatelessServiceContext();

            var gen = new ProxyGenerator();
            var impl = (IFakeService) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IFakeService),
                new Type[0],
                Mock.Of<IFakeService>(),
                new LoggingServiceProxyInterceptor(client, ctx, "other://uri.test"));

            impl.TestServiceMethod();
            client.Flush();
            List<DependencyTelemetry> dependencyTelemetries =
                telemetryChannel.Telemetry.OfType<DependencyTelemetry>().ToList();
            dependencyTelemetries.Should().ContainSingle();
            DependencyTelemetry dependencyTelemetry = dependencyTelemetries[0];
            (dependencyTelemetry.Success ?? true).Should().BeTrue();
            dependencyTelemetry.Type.Should().Be("ServiceFabricRemoting");
            dependencyTelemetry.Target.Should().Be("other://uri.test");
            dependencyTelemetry.Data.Should().StartWith("other://uri.test");
            dependencyTelemetry.Data.Should().Contain(nameof(IFakeService));
            dependencyTelemetry.Data.Should().Contain(nameof(IFakeService.TestServiceMethod));

            telemetryChannel.Telemetry.OfType<ExceptionTelemetry>().Should().BeEmpty();
        }
    }
}

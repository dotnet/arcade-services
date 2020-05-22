using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Reflection;
using Castle.DynamicProxy;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Moq;
using Xunit;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    public class LoggingServiceProxyInterceptorTests
    {
        [Fact]
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

            var invocationException = Assert.Throws<InvalidOperationException>(() => impl.TestServiceMethod());
            client.Flush();
            List<DependencyTelemetry> dependencyTelemetries =
                telemetryChannel.Telemetry.OfType<DependencyTelemetry>().ToList();
            Assert.Single(dependencyTelemetries);
            DependencyTelemetry dependencyTelemetry = dependencyTelemetries[0];
            Assert.False(dependencyTelemetry.Success);
            Assert.Equal("ServiceFabricRemoting", dependencyTelemetry.Type);
            Assert.Equal("other://uri.test", dependencyTelemetry.Target);
            Assert.StartsWith("other://uri.test", dependencyTelemetry.Data);
            Assert.Contains(nameof(IFakeService), dependencyTelemetry.Data, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(nameof(IFakeService.TestServiceMethod),
                dependencyTelemetry.Data,
                StringComparison.OrdinalIgnoreCase);

            List<ExceptionTelemetry> exceptionTelemetries =
                telemetryChannel.Telemetry.OfType<ExceptionTelemetry>().ToList();
            Assert.Single(exceptionTelemetries);
            ExceptionTelemetry exceptionTelemetry = exceptionTelemetries[0];
            Assert.Same(invocationException, exceptionTelemetry.Exception);
        }

        [Fact]
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

            var invocationException = Assert.Throws<AggregateException>(() => impl.TestServiceMethod());
            client.Flush();
            List<DependencyTelemetry> dependencyTelemetries =
                telemetryChannel.Telemetry.OfType<DependencyTelemetry>().ToList();
            Assert.Single(dependencyTelemetries);
            DependencyTelemetry dependencyTelemetry = dependencyTelemetries[0];
            Assert.False(dependencyTelemetry.Success);
            Assert.Equal("ServiceFabricRemoting", dependencyTelemetry.Type);
            Assert.Equal("other://uri.test", dependencyTelemetry.Target);
            Assert.StartsWith("other://uri.test", dependencyTelemetry.Data);
            Assert.Contains(nameof(IFakeService), dependencyTelemetry.Data, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(nameof(IFakeService.TestServiceMethod),
                dependencyTelemetry.Data,
                StringComparison.OrdinalIgnoreCase);

            List<ExceptionTelemetry> exceptionTelemetries =
                telemetryChannel.Telemetry.OfType<ExceptionTelemetry>().ToList();
            Assert.Single(exceptionTelemetries);
            ExceptionTelemetry exceptionTelemetry = exceptionTelemetries[0];
            Assert.Same(invocationException, exceptionTelemetry.Exception);
        }

        [Fact]
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

            var invocationException = Assert.Throws<InvalidOperationException>(() => impl.TestServiceMethod());
            client.Flush();
            List<DependencyTelemetry> dependencyTelemetries =
                telemetryChannel.Telemetry.OfType<DependencyTelemetry>().ToList();
            Assert.Single(dependencyTelemetries);
            DependencyTelemetry dependencyTelemetry = dependencyTelemetries[0];
            Assert.False(dependencyTelemetry.Success);
            Assert.Equal("ServiceFabricRemoting", dependencyTelemetry.Type);
            Assert.Equal("other://uri.test", dependencyTelemetry.Target);
            Assert.StartsWith("other://uri.test", dependencyTelemetry.Data);
            Assert.Contains(nameof(IFakeService), dependencyTelemetry.Data, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(nameof(IFakeService.TestServiceMethod),
                dependencyTelemetry.Data,
                StringComparison.OrdinalIgnoreCase);

            List<ExceptionTelemetry> exceptionTelemetries =
                telemetryChannel.Telemetry.OfType<ExceptionTelemetry>().ToList();
            Assert.Single(exceptionTelemetries);
            ExceptionTelemetry exceptionTelemetry = exceptionTelemetries[0];
            Assert.Same(invocationException, exceptionTelemetry.Exception);
        }

        [Fact]
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
            Assert.Single(dependencyTelemetries);
            DependencyTelemetry dependencyTelemetry = dependencyTelemetries[0];
            Assert.True(dependencyTelemetry.Success ?? true);
            Assert.Equal("ServiceFabricRemoting", dependencyTelemetry.Type);
            Assert.Equal("other://uri.test", dependencyTelemetry.Target);
            Assert.StartsWith("other://uri.test", dependencyTelemetry.Data);
            Assert.Contains(nameof(IFakeService), dependencyTelemetry.Data, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(nameof(IFakeService.TestServiceMethod),
                dependencyTelemetry.Data,
                StringComparison.OrdinalIgnoreCase);

            Assert.Empty(telemetryChannel.Telemetry.OfType<ExceptionTelemetry>());
        }
    }
}

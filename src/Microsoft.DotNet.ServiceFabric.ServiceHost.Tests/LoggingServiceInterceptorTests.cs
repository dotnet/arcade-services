using System;
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
    public class LoggingServiceInterceptorTests
    {
        [Fact]
        public void LogsCorrectUrl()
        {
            var telemetryChannel = new FakeChannel();
            var config = new TelemetryConfiguration("00000000-0000-0000-0000-000000000001", telemetryChannel);
            var client = new TelemetryClient(config);

            Mock<ServiceContext> ctx = MockBuilder.MockServiceContext();

            var gen = new ProxyGenerator();
            var impl = (IFakeService) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IFakeService),
                new Type[0],
                Mock.Of<IFakeService>(),
                new LoggingServiceInterceptor(ctx.Object, client));

            impl.TestServiceMethod();
            client.Flush();
            RequestTelemetry requestTelemetry = telemetryChannel.Telemetry.OfType<RequestTelemetry>().FirstOrDefault();
            Assert.NotNull(requestTelemetry);
            Assert.True(requestTelemetry.Success ?? true);
            Assert.StartsWith(ctx.Object.ServiceName.AbsoluteUri, requestTelemetry.Url.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(nameof(IFakeService), requestTelemetry.Url.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ExceptionLogsFailedRequest()
        {
            var telemetryChannel = new FakeChannel();
            var config = new TelemetryConfiguration("00000000-0000-0000-0000-000000000001", telemetryChannel);
            var client = new TelemetryClient(config);

            Mock<ServiceContext> ctx = MockBuilder.MockServiceContext();

            Mock<IFakeService> fakeService = new Mock<IFakeService>();
            fakeService.Setup(s => s.TestServiceMethod()).Throws(new InvalidOperationException("Test Exception Text"));

            var gen = new ProxyGenerator();
            var impl = (IFakeService) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IFakeService),
                new Type[0],
                fakeService.Object,
                new LoggingServiceInterceptor(ctx.Object, client));
            
            var ex = Assert.ThrowsAny<InvalidOperationException>(() => impl.TestServiceMethod());
            Assert.Equal("Test Exception Text", ex.Message);
            
            client.Flush();
            RequestTelemetry requestTelemetry = telemetryChannel.Telemetry.OfType<RequestTelemetry>().FirstOrDefault();
            Assert.NotNull(requestTelemetry);
            Assert.False(requestTelemetry.Success);
            ExceptionTelemetry exceptionTelemetry = telemetryChannel.Telemetry.OfType<ExceptionTelemetry>().FirstOrDefault();
            Assert.NotNull(exceptionTelemetry);
            Assert.Same(ex, exceptionTelemetry.Exception);
        }
    }
}

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Microsoft.DotNet.Internal.Health.Tests
{
    public class HealthReportingTests
    {
        public async Task SimpleHealthReportReportsToTableEndpoint()
        {
            MockHandler handler = new MockHandler(req =>
            {

            });
            Mock<IHttpMessageHandlerFactory> factory = new Mock<IHttpMessageHandlerFactory>();
            factory.Setup(f => f.CreateHandler(It.IsAny<string>())).Returns(handler);
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(factory.Object);
            collection.AddHttpClient();
            collection.AddHealthReporting(b =>
            {
                b.AddAzureTable("http://table.example.test/myTable?someQueryStuff");
            });

            await using ServiceProvider services = collection.BuildServiceProvider();
            IHealthReport<HealthReportingTests> report =
                services.GetRequiredService<IHealthReport<HealthReportingTests>>();

            await report.UpdateStatus("TEST-SUB-STATUS", HealthStatus.Healthy, "TEST STATUS MESSAGES");
        }
    }

    public class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _callback;

        public MockHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback)
        {
            _callback = callback;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _callback(request);
        }
    }
}

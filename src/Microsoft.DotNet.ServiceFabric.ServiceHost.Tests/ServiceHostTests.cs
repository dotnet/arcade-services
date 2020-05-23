using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Rest;
using Xunit;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    public class ServiceHostTests
    {
        [Fact]
        public void HttpExceptionsAreAugmentedByRichTelemetry()
        {
            Environment.SetEnvironmentVariable("ENVIRONMENT", "XUnit");
            ServiceCollection services = new ServiceCollection();
            var channel = new FakeChannel();
            services.AddSingleton<ITelemetryChannel>(channel);
            ServiceHost.ConfigureDefaultServices(services);


            var provider = services.BuildServiceProvider();

            var client = provider.GetRequiredService<TelemetryClient>();
            var httpOperationException = new HttpOperationException
            {
                Response = new HttpResponseMessageWrapper(
                    new HttpResponseMessage((HttpStatusCode) 418),
                    "Test content"),
            };
            client.TrackException(httpOperationException);
            client.Flush();

            var exceptionTelemetries = channel.Telemetry.OfType<ExceptionTelemetry>().ToList();
            Assert.Single(exceptionTelemetries);
            var exceptionTelemetry = exceptionTelemetries[0];
            Assert.True(exceptionTelemetry.Properties.TryGetValue("statusCode", out var statusCodeTest));
            Assert.Equal("418", statusCodeTest);
            Assert.True(exceptionTelemetry.Properties.TryGetValue("responseText", out var responseText));
            Assert.Equal("Test content", responseText);
        }

        [Fact]
        public void HttpExceptionsContentIsTruncatedByRichTelemetry()
        {
            Environment.SetEnvironmentVariable("ENVIRONMENT", "XUnit");
            ServiceCollection services = new ServiceCollection();
            var channel = new FakeChannel();
            services.AddSingleton<ITelemetryChannel>(channel);
            ServiceHost.ConfigureDefaultServices(services);


            var provider = services.BuildServiceProvider();

            var client = provider.GetRequiredService<TelemetryClient>();
            string bigContent = "Test content" + new string('*', 10000);
            var httpOperationException = new HttpOperationException
            {
                Response = new HttpResponseMessageWrapper(
                    new HttpResponseMessage((HttpStatusCode) 418),
                    bigContent),
            };
            client.TrackException(httpOperationException);
            client.Flush();

            var exceptionTelemetries = channel.Telemetry.OfType<ExceptionTelemetry>().ToList();
            Assert.Single(exceptionTelemetries);
            var exceptionTelemetry = exceptionTelemetries[0];
            Assert.True(exceptionTelemetry.Properties.TryGetValue("responseText", out var responseText));
            Assert.StartsWith("Test content", responseText);
            Assert.True(responseText.Length < bigContent.Length, "responseText.Length < bigContent.Length");
        }
    }
}

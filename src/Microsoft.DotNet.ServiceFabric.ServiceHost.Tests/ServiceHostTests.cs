using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Rest;
using NUnit.Framework;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    [TestFixture]
    public class ServiceHostTests
    {
        [Test]
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
            exceptionTelemetries.Should().ContainSingle();
            var exceptionTelemetry = exceptionTelemetries[0];
            exceptionTelemetry.Properties.TryGetValue("statusCode", out var statusCodeTest).Should().BeTrue();
            statusCodeTest.Should().Be("418");
            exceptionTelemetry.Properties.TryGetValue("responseText", out var responseText).Should().BeTrue();
            responseText.Should().Be("Test content");
        }

        [Test]
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
            exceptionTelemetries.Should().ContainSingle();
            var exceptionTelemetry = exceptionTelemetries[0];
            exceptionTelemetry.Properties.TryGetValue("responseText", out var responseText).Should().BeTrue();
            responseText.Should().StartWith("Test content");
            (responseText.Length < bigContent.Length).Should().BeTrue();
        }
    }
}

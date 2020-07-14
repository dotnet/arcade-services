using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using NUnit.Framework;

namespace Maestro.Web.Tests
{
    [TestFixture]
    public class LoggingConfigurationTests
    {
        [Test]
        public void LoggingWithScopes()
        {
            Environment.SetEnvironmentVariable("ENVIRONMENT", Environments.Development);
            Mock<ITelemetryChannel> channel = new Mock<ITelemetryChannel>();
            List<ITelemetry> telemetry = new List<ITelemetry>();
            channel.Setup(s => s.Send(Capture.In(telemetry)));

            var config = new ConfigurationBuilder();
            var collection = new ServiceCollection();
            collection.AddSingleton(channel.Object);
            collection.AddSingleton<OperationManager>();
            // The only scenario we are worried about is when running in the ServiceHost
            ServiceHost.ConfigureDefaultServices(collection);

            collection.AddSingleton<IConfiguration>(config.Build());
            collection.AddSingleton<Startup>();
            using ServiceProvider outerProvider = collection.BuildServiceProvider();
            var startup = outerProvider.GetRequiredService<Startup>();
            startup.ConfigureServices(collection);

            using ServiceProvider innerProvider = collection.BuildServiceProvider();
            var logger = innerProvider.GetRequiredService<ILogger<DependencyRegistrationTests>>();
            var operations = innerProvider.GetRequiredService<OperationManager>();
            var tc = innerProvider.GetRequiredService<TelemetryClient>();
            Activity.Current = null;
            // AppInsights behaves very oddly if the ActivityId is W3C
            // It's not ideal for a test to mess with static state, but we need to ensure this works correctly
            Activity.DefaultIdFormat = ActivityIdFormat.Hierarchical;
            using (tc.StartOperation<RequestTelemetry>("Fake operation"))
            {
                logger.LogError("Outside");
                using (operations.BeginOperation("TEST-SCOPE:{TEST_KEY}", "TEST_VALUE"))
                {
                    logger.LogError("Something: {TEST_SOMETHING_KEY}", "TEST_SOMETHING_VALUE");
                    logger.LogError("Else");
                }

                logger.LogError("Outside again");
            }

            innerProvider.GetRequiredService<TelemetryClient>().Flush();
            List<TraceTelemetry> traces = telemetry.OfType<TraceTelemetry>().ToList();
            traces.Should().HaveCount(4);

            {
                // The operation id should stay constant, it's the root
                string[] opIds = traces.Select(t => t.Context?.Operation?.Id).ToArray();
                opIds[0].Should().NotBeNull();
                opIds[1].Should().Be(opIds[0]);
                opIds[2].Should().Be(opIds[1]);
                opIds[3].Should().Be(opIds[2]);
            }

            {
                // The parent ids should flow with the operation start/stop
                var parentIds = traces.Select(t => t.Context?.Operation?.ParentId).ToArray();
                parentIds[0].Should().NotBeNull();
                parentIds[1].Should().NotBe(parentIds[0]);
                parentIds[1].Should().StartWith(parentIds[0]);
                parentIds[2].Should().Be(parentIds[1]);
                parentIds[3].Should().NotBe(parentIds[2]);
                parentIds[3].Should().Be(parentIds[0]);
            }

            // The things in the operation should flow the properties from the BeginOperation
            traces[1].Properties.GetValueOrDefault("TEST_KEY").Should().Be("TEST_VALUE");

            // The things outside the operation should not have those properties
            traces[3].Properties.Should().NotContainKey("TEST_VALUE");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using Xunit;

namespace Maestro.Web.Tests
{
    public class DependencyRegistrationTests
    {
        [Fact]
        public void AreDependenciesRegistered()
        {
            Environment.SetEnvironmentVariable("ENVIRONMENT", Environments.Development);

            var config = new ConfigurationBuilder();
            var collection = new ServiceCollection();

            // The only scenario we are worried about is when running in the ServiceHost
            ServiceHost.ConfigureDefaultServices(collection);

            collection.AddSingleton<IConfiguration>(config.Build());
            collection.AddSingleton<Startup>();
            using ServiceProvider provider = collection.BuildServiceProvider();
            var startup = provider.GetRequiredService<Startup>();

            IEnumerable<Type> controllerTypes = typeof(Startup).Assembly.ExportedTypes
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t));

            Assert.True(DependencyInjectionValidation.IsDependencyResolutionCoherent(
                    s =>
                    {
                        foreach (ServiceDescriptor descriptor in collection)
                        {
                            s.Add(descriptor);
                        }

                        startup.ConfigureServices(s);
                    },
                    out string message,
                    additionalScopedTypes: controllerTypes),
                message);
        }
    }

    public class LoggingConfigurationTests
    {
        [Fact]
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
            Assert.Equal(4, traces.Count);

            {
                // The operation id should stay constant, it's the root
                string[] opIds = traces.Select(t => t.Context?.Operation?.Id).ToArray();
                Assert.NotNull(opIds[0]);
                Assert.Equal(opIds[0], opIds[1]);
                Assert.Equal(opIds[1], opIds[2]);
                Assert.Equal(opIds[2], opIds[3]);
            }

            {
                // The parent ids should flow with the operation start/stop
                var parentIds = traces.Select(t => t.Context?.Operation?.ParentId).ToArray();
                Assert.NotNull(parentIds[0]);
                Assert.NotEqual(parentIds[0], parentIds[1]);
                Assert.StartsWith(parentIds[0], parentIds[1]);
                Assert.Equal(parentIds[1], parentIds[2]);
                Assert.NotEqual(parentIds[2], parentIds[3]);
                Assert.Equal(parentIds[0], parentIds[3]);
            }

            // The things in the operation should flow the properties from the BeginOperation
            Assert.Equal("TEST_VALUE", traces[1].Properties.GetValueOrDefault("TEST_KEY"));

            // The things outside the operation should not have those properties
            Assert.DoesNotContain("TEST_VALUE", traces[3].Properties);
        }
    }
}

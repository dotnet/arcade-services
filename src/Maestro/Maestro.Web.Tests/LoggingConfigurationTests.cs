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
    [TestFixture, NonParallelizable]
    public class LoggingConfigurationTests
    {
        private class TestData
            : IDisposable
        {
            private readonly ServiceProvider _outerProvider;
            public ILogger Logger { get; }
            public OperationManager OperationManager { get; }
            public TelemetryClient TelemetryClient { get; }
            public List<ITelemetry> TelemetryLogged { get; }
            public ServiceProvider Provider { get; }

            public TestData(
                ILogger logger,
                OperationManager operationManager,
                TelemetryClient telemetryClient,
                List<ITelemetry> telemetryLogged,
                ServiceProvider provider,
                ServiceProvider outerProvider)
            {
                _outerProvider = outerProvider;
                Logger = logger;
                OperationManager = operationManager;
                TelemetryClient = telemetryClient;
                TelemetryLogged = telemetryLogged;
                Provider = provider;
            }

            public void Dispose()
            {
                Provider.Dispose();
                _outerProvider.Dispose();
            }
        }

        private sealed class TrackedDisposable : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private static TestData Setup()
        {
            Environment.SetEnvironmentVariable("ENVIRONMENT", Environments.Development);
            var channel = new Mock<ITelemetryChannel>();
            var telemetry = new List<ITelemetry>();
            channel.Setup(s => s.Send(Capture.In(telemetry)));

            var config = new ConfigurationBuilder();
            var collection = new ServiceCollection();
            collection.AddSingleton(channel.Object);
            collection.AddSingleton<OperationManager>();
            collection.AddScoped<TrackedDisposable>();
            // The only scenario we are worried about is when running in the ServiceHost
            ServiceHost.ConfigureDefaultServices(collection);

            collection.AddSingleton<IConfiguration>(config.Build());
            collection.AddSingleton<Startup>();
            ServiceProvider outerProvider = collection.BuildServiceProvider();
            var startup = outerProvider.GetRequiredService<Startup>();
            startup.ConfigureServices(collection);

            ServiceProvider innerProvider = collection.BuildServiceProvider();
            var logger = innerProvider.GetRequiredService<ILogger<DependencyRegistrationTests>>();
            var operations = innerProvider.GetRequiredService<OperationManager>();
            var tc = innerProvider.GetRequiredService<TelemetryClient>();

            Activity.Current = null;
            // AppInsights behaves very oddly if the ActivityId is W3C
            // It's not ideal for a test to mess with static state, but we need to ensure this works correctly
            Activity.DefaultIdFormat = ActivityIdFormat.Hierarchical;

            return new TestData(logger, operations, tc, telemetry, innerProvider, outerProvider);
        }
        
        [Test]
        public void FullScopeDisposesScopedDependencies()
        {
            using TestData data = Setup();
            TrackedDisposable toDispose;
            using (var op = data.OperationManager.BeginOperation("TEST-SCOPE:{TEST_KEY}", "TEST_VALUE"))
            {
                toDispose = op.ServiceProvider.GetRequiredService<TrackedDisposable>();
            }

            toDispose.Disposed.Should().BeTrue();
        }
        
        [Test]
        public void LoggingScopeDoesNotDisposeScopedDependencies()
        {
            using TestData data = Setup();
            TrackedDisposable toDispose;
            using (var op = data.OperationManager.BeginLoggingScope("TEST-SCOPE:{TEST_KEY}", "TEST_VALUE"))
            {
                toDispose = op.ServiceProvider.GetRequiredService<TrackedDisposable>();
            }

            toDispose.Disposed.Should().BeFalse();
        }

        [Test]
        public void LoggingWithLoggingScopes()
        {
            using TestData data = Setup();
            using (data.TelemetryClient.StartOperation<RequestTelemetry>("Fake operation"))
            {
                data.Logger.LogError("Outside");
                using (var op = data.OperationManager.BeginLoggingScope("TEST-SCOPE:{TEST_KEY}", "TEST_VALUE"))
                {
                    data.Logger.LogError("Something: {TEST_SOMETHING_KEY}", "TEST_SOMETHING_VALUE");
                    data.Logger.LogError("Else");
                }

                data.Logger.LogError("Outside again");
            }

            data.TelemetryClient.Flush();
            List<TraceTelemetry> traces = data.TelemetryLogged.OfType<TraceTelemetry>().ToList();
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

        [Test]
        public void LoggingWithFullScopes()
        {
            using TestData data = Setup();
            using (data.TelemetryClient.StartOperation<RequestTelemetry>("Fake operation"))
            {
                data.Logger.LogError("Outside");
                using (var op = data.OperationManager.BeginOperation("TEST-SCOPE:{TEST_KEY}", "TEST_VALUE"))
                {
                    data.Logger.LogError("Something: {TEST_SOMETHING_KEY}", "TEST_SOMETHING_VALUE");
                    data.Logger.LogError("Else");
                }

                data.Logger.LogError("Outside again");
            }

            data.TelemetryClient.Flush();
            List<TraceTelemetry> traces = data.TelemetryLogged.OfType<TraceTelemetry>().ToList();
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

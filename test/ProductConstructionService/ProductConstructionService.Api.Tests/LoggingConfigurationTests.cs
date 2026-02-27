// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using AwesomeAssertions;
using Maestro.Common.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Common;
using Moq;

namespace ProductConstructionService.Api.Tests;

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

    private static async Task<TestData> Setup()
    {
        var channel = new Mock<ITelemetryChannel>();
        var telemetry = new List<ITelemetry>();
        channel.Setup(s => s.Send(Capture.In(telemetry)));

        var builder = ApiTestConfiguration.CreateTestHostBuilder();

        await builder.ConfigurePcs(
            addKeyVault: false,
            addSwagger: false);

        builder.Services.AddSingleton(channel.Object);
        builder.Services.AddSingleton<OperationManager>();
        builder.Services.AddScoped<TrackedDisposable>();
        builder.Services.AddSingleton<ITelemetryRecorder, NoTelemetryRecorder>();

        ServiceProvider outerProvider = builder.Services.BuildServiceProvider();
        ServiceProvider innerProvider = builder.Services.BuildServiceProvider();

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
    public async Task FullScopeDisposesScopedDependencies()
    {
        using TestData data = await Setup();
        TrackedDisposable toDispose;
        using (var op = data.OperationManager.BeginOperation("TEST-SCOPE:{TEST_KEY}", "TEST_VALUE"))
        {
            toDispose = op.ServiceProvider.GetRequiredService<TrackedDisposable>();
        }

        toDispose.Disposed.Should().BeTrue();
    }

    [Test]
    public async Task LoggingScopeDoesNotDisposeScopedDependencies()
    {
        using TestData data = await Setup();
        TrackedDisposable toDispose;
        using (var op = data.OperationManager.BeginLoggingScope("TEST-SCOPE:{TEST_KEY}", "TEST_VALUE"))
        {
            toDispose = op.ServiceProvider.GetRequiredService<TrackedDisposable>();
        }

        toDispose.Disposed.Should().BeFalse();
    }

    [Test]
    public async Task LoggingWithLoggingScopes()
    {
        using TestData data = await Setup();
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
        var traces = data.TelemetryLogged.OfType<TraceTelemetry>().ToList();
        traces.Should().HaveCount(4);

        {
            // The operation id should stay constant, it's the root
            var opIds = traces.Select(t => t.Context?.Operation?.Id).ToArray();
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
    public async Task LoggingWithFullScopes()
    {
        using TestData data = await Setup();
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
        var traces = data.TelemetryLogged.OfType<TraceTelemetry>().ToList();
        traces.Should().HaveCount(4);

        {
            // The operation id should stay constant, it's the root
            var opIds = traces.Select(t => t.Context?.Operation?.Id).ToArray();
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

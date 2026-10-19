// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using AwesomeAssertions;
using Azure.Core;
using Azure.Storage.Queues;
using Maestro.Common.Telemetry;
using Maestro.Services.Common;
using Maestro.WorkItems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Maestro.WorkItem.Tests;

public class WorkItemConsumerHealthCheckTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private CancellationTokenSource _applicationStarted = null!;
    private CancellationTokenSource _applicationStopping = null!;
    private Mock<IHostApplicationLifetime> _applicationLifetime = null!;

    [SetUp]
    public void SetUp()
    {
        _applicationStarted = new CancellationTokenSource();
        _applicationStopping = new CancellationTokenSource();
        _applicationLifetime = new Mock<IHostApplicationLifetime>();
        _applicationLifetime.SetupGet(l => l.ApplicationStarted).Returns(_applicationStarted.Token);
        _applicationLifetime.SetupGet(l => l.ApplicationStopping).Returns(_applicationStopping.Token);
    }

    [TearDown]
    public void TearDown()
    {
        _applicationStarted.Dispose();
        _applicationStopping.Dispose();
    }

    [Test]
    public async Task ConsumersNotYetStartedAreHealthyDuringHostStartup()
    {
        using var consumer = new TestConsumer("starting", "queue", _ => Task.CompletedTask);
        var check = CreateHealthCheck(consumer);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Test]
    public async Task ConsumerNotStartedAfterHostStartupIsUnhealthy()
    {
        _applicationStarted.Cancel();
        using var consumer = new TestConsumer("not-started", "queue", _ => Task.CompletedTask);
        var check = CreateHealthCheck(consumer);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("not-started (queue)");
    }

    [Test]
    public async Task RunningConsumerIsHealthyWhileWaitingForWork()
    {
        _applicationStarted.Cancel();
        using var consumer = new TestConsumer("waiting", "queue",
            cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        var check = CreateHealthCheck(consumer);
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

            result.Status.Should().Be(HealthStatus.Healthy);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        }
    }

    [TestCase(TaskStatus.RanToCompletion)]
    [TestCase(TaskStatus.Faulted)]
    [TestCase(TaskStatus.Canceled)]
    public async Task UnexpectedConsumerExitIsUnhealthyEvenIfOtherConsumersAreRunning(TaskStatus completionStatus)
    {
        _applicationStarted.Cancel();
        var execution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopped = new TestConsumer("stopped", "codeflow-queue", _ => execution.Task);
        using var running = new TestConsumer("running", "default-queue",
            cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        var check = CreateHealthCheck(stopped, running);
        await stopped.StartAsync(CancellationToken.None);
        await running.StartAsync(CancellationToken.None);
        switch (completionStatus)
        {
            case TaskStatus.RanToCompletion:
                execution.SetResult();
                break;
            case TaskStatus.Faulted:
                execution.SetException(new InvalidOperationException("Consumer failed"));
                break;
            case TaskStatus.Canceled:
                execution.SetCanceled();
                break;
        }
        await stopped.ExecuteTask!.WaitAsync(TestTimeout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        try
        {
            HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

            result.Status.Should().Be(HealthStatus.Unhealthy);
            result.Description.Should().Contain("stopped (codeflow-queue)").And.NotContain("running (default-queue)");
        }
        finally
        {
            await running.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
        }
    }

    [Test]
    public async Task ConsumerExitDuringHostShutdownDoesNotFailLiveness()
    {
        _applicationStarted.Cancel();
        using var consumer = new TestConsumer("stopping", "queue", _ => Task.CompletedTask);
        var check = CreateHealthCheck(consumer);
        await consumer.StartAsync(CancellationToken.None);
        await consumer.ExecuteTask!.WaitAsync(TestTimeout);
        _applicationStopping.Cancel();

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Test]
    public async Task QueueRegistrationIncludesEveryConsumerInLivenessChecks()
    {
        _applicationStarted.Cancel();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
        });
        builder.AddWorkItemQueues(Mock.Of<TokenCredential>(), waitForInitialization: true,
            new Dictionary<string, (int Count, string WorkItemType)>
            {
                { "default-queue", (2, "Default") },
                { "codeflow-queue", (1, "CodeFlow") },
            });
        builder.Services.AddSingleton(_applicationLifetime.Object);
        builder.Services.AddSingleton(Mock.Of<IReplicaWorkItemProcessorStateStore>());
        builder.Services.AddSingleton(Mock.Of<QueueServiceClient>());
        builder.Services.AddSingleton(Mock.Of<IMetricRecorder>());
        builder.Services.AddSingleton(Mock.Of<ITelemetryRecorder>());
        using var provider = builder.Services.BuildServiceProvider();

        HealthReport report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Tags.Contains(ServiceDefaultsExtensions.LivenessHealthCheckTag));

        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries.Should().ContainSingle();
        report.Entries["WorkItemConsumers"].Description.Should()
            .Contain("DefaultWorkItemConsumer_0 (default-queue)")
            .And.Contain("DefaultWorkItemConsumer_1 (default-queue)")
            .And.Contain("CodeFlowWorkItemConsumer_0 (codeflow-queue)");
    }

    [Test]
    public async Task HealthEndpointsReturnServiceUnavailableAfterConsumerExits()
    {
        var execution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var consumer = new TestConsumer("consumer", "queue", cancellationToken => execution.Task.WaitAsync(cancellationToken));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IHostedService>(consumer);
        builder.Services.AddHealthChecks()
            .AddCheck<WorkItemConsumerHealthCheck>("WorkItemConsumers", tags: [ServiceDefaultsExtensions.LivenessHealthCheckTag]);
        await using var app = builder.Build();
        app.MapDefaultEndpoints();
        await app.StartAsync().WaitAsync(TestTimeout);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TestTimeout };
            using var healthy = await client.GetAsync(ServiceDefaultsExtensions.AlivenessEndpointPath);
            healthy.StatusCode.Should().Be(HttpStatusCode.OK);

            execution.SetResult();
            await consumer.ExecuteTask!.WaitAsync(TestTimeout);
            using var unhealthy = await client.GetAsync(ServiceDefaultsExtensions.AlivenessEndpointPath);
            using var notReady = await client.GetAsync(ServiceDefaultsExtensions.HealthEndpointPath);

            unhealthy.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            notReady.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            await app.StopAsync().WaitAsync(TestTimeout);
        }
    }

    private WorkItemConsumerHealthCheck CreateHealthCheck(params IHostedService[] services) =>
        new(services, _applicationLifetime.Object, NullLogger<WorkItemConsumerHealthCheck>.Instance);

    private sealed class TestConsumer(string consumerId, string queueName, Func<CancellationToken, Task> executeAsync)
        : WorkItemConsumer(
            consumerId,
            queueName,
            NullLogger<WorkItemConsumer>.Instance,
            Options.Create(new WorkItemConsumerOptions
            {
                QueuePollTimeout = TimeSpan.FromSeconds(1),
                QueueMessageInvisibilityTime = TimeSpan.FromMinutes(15),
                MaxWorkItemRetries = 3,
            }),
            new WorkItemScopeManager(Mock.Of<IServiceProvider>(), new WorkItemAdmissionGate()),
            Mock.Of<QueueServiceClient>(),
            Mock.Of<IMetricRecorder>(),
            Mock.Of<ITelemetryRecorder>())
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => executeAsync(stoppingToken);
    }
}

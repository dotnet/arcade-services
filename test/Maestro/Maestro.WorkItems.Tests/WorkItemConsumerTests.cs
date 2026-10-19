// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using AwesomeAssertions;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Maestro.Common.Telemetry;
using Maestro.WorkItems;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Maestro.WorkItem.Tests;

public class WorkItemConsumerTests
{
    private const int MaxWorkItemRetries = 3;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly WorkItemConsumerOptions _options = new()
    {
        QueuePollTimeout = TimeSpan.FromSeconds(1),
        QueueMessageInvisibilityTime = TimeSpan.FromMinutes(15),
        MaxWorkItemRetries = MaxWorkItemRetries,
    };

    private Mock<QueueClient> _queueClient = null!;
    private WorkItemAdmissionGate _admissionGate = null!;
    private ServiceProvider _serviceProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _queueClient = new Mock<QueueClient>(MockBehavior.Strict);
        _queueClient
            .Setup(q => q.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());
        _admissionGate = new WorkItemAdmissionGate();
        _admissionGate.Open();
    }

    [TearDown]
    public void TearDown() => _serviceProvider?.Dispose();

    [TestCase(1)]
    [TestCase(MaxWorkItemRetries)]
    public async Task WorkItemTimeoutUsesRetryPolicyAndConsumerProcessesNextMessage(long attempt)
    {
        QueueMessage timedOutMessage = CreateMessage("timed-out", attempt);
        QueueMessage nextMessage = CreateMessage("next", 1);
        _queueClient
            .SetupSequence(q => q.ReceiveMessageAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(timedOutMessage, Mock.Of<Response>()))
            .ReturnsAsync(Response.FromValue(nextMessage, Mock.Of<Response>()));
        var nextMessageDeleted = SignalWhenMessageDeleted(nextMessage);
        int processed = 0;
        using var consumer = CreateConsumer((_, _) =>
        {
            if (++processed == 1)
            {
                throw new TaskCanceledException("HTTP request timed out", new TimeoutException());
            }
            else
            {
                return Task.FromResult(true);
            }
        });

        await RunUntilAsync(consumer, nextMessageDeleted.Task);

        nextMessageDeleted.Task.IsCompletedSuccessfully.Should().BeTrue();
        processed.Should().Be(2);
        _queueClient.Verify(q => q.DeleteMessageAsync(timedOutMessage.MessageId, timedOutMessage.PopReceipt,
            It.IsAny<CancellationToken>()), attempt >= MaxWorkItemRetries ? Times.Once() : Times.Never());
        _queueClient.Verify(q => q.DeleteMessageAsync(nextMessage.MessageId, nextMessage.PopReceipt,
            It.IsAny<CancellationToken>()), Times.Once);
        _admissionGate.ActiveAdmissionCount.Should().Be(0);
    }

    [Test]
    public async Task QueueReceiveTimeoutDoesNotStopConsumer()
    {
        QueueMessage message = CreateMessage("next", 1);
        _queueClient
            .SetupSequence(q => q.ReceiveMessageAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("Queue request timed out", new TimeoutException()))
            .ReturnsAsync(Response.FromValue(message, Mock.Of<Response>()));
        var messageDeleted = SignalWhenMessageDeleted(message);
        using var consumer = CreateConsumer((_, _) => Task.FromResult(true));

        await RunUntilAsync(consumer, messageDeleted.Task);

        messageDeleted.Task.IsCompletedSuccessfully.Should().BeTrue();
        _queueClient.Verify(q => q.ReceiveMessageAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _admissionGate.ActiveAdmissionCount.Should().Be(0);
    }

    [Test]
    public async Task HostShutdownStopsConsumerWithoutDeletingCanceledWorkItem()
    {
        QueueMessage message = CreateMessage("canceled", MaxWorkItemRetries);
        _queueClient
            .Setup(q => q.ReceiveMessageAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(message, Mock.Of<Response>()));
        var processingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var consumer = CreateConsumer(async (_, cancellationToken) =>
        {
            processingStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        });

        await RunUntilAsync(consumer, processingStarted.Task);

        processingStarted.Task.IsCompletedSuccessfully.Should().BeTrue();
        consumer.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        _queueClient.Verify(q => q.ReceiveMessageAsync(It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
        _queueClient.Verify(q => q.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _admissionGate.ActiveAdmissionCount.Should().Be(0);
    }

    private TaskCompletionSource SignalWhenMessageDeleted(QueueMessage message)
    {
        var deleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queueClient
            .Setup(q => q.DeleteMessageAsync(message.MessageId, message.PopReceipt, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                _admissionGate.Close();
                deleted.SetResult();
            })
            .ReturnsAsync(Mock.Of<Response>());
        return deleted;
    }

    private WorkItemConsumer CreateConsumer(Func<TestWorkItem, CancellationToken, Task<bool>> processAsync)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new TelemetryClient(new() { DisableTelemetry = true }));
        services.AddWorkItemProcessor<TestWorkItem, TestWorkItemProcessor>(_ => new TestWorkItemProcessor(processAsync));
        _serviceProvider = services.BuildServiceProvider();

        var queueServiceClient = new Mock<QueueServiceClient>();
        queueServiceClient.Setup(q => q.GetQueueClient("test-queue")).Returns(_queueClient.Object);
        var telemetryRecorder = new Mock<ITelemetryRecorder>();
        telemetryRecorder
            .Setup(t => t.RecordWorkItemCompletion(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>()))
            .Returns(() => Mock.Of<ITelemetryScope>());

        return new WorkItemConsumer(
            "test-consumer",
            "test-queue",
            NullLogger<WorkItemConsumer>.Instance,
            Options.Create(_options),
            new WorkItemScopeManager(_serviceProvider, _admissionGate),
            queueServiceClient.Object,
            Mock.Of<IMetricRecorder>(),
            telemetryRecorder.Object);
    }

    private static async Task RunUntilAsync(WorkItemConsumer consumer, Task signal)
    {
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            await Task.WhenAny(signal, consumer.ExecuteTask!).WaitAsync(TestTimeout);
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TestTimeout);
            await consumer.StopAsync(timeout.Token);
        }
    }

    private static QueueMessage CreateMessage(string id, long attempt) =>
        QueuesModelFactory.QueueMessage(
            id,
            "pop-receipt",
            JsonSerializer.Serialize(new TestWorkItem(), WorkItemConfiguration.JsonSerializerOptions),
            attempt,
            insertedOn: DateTimeOffset.UtcNow);

    public class TestWorkItem : WorkItems.WorkItem;

    private class TestWorkItemProcessor(Func<TestWorkItem, CancellationToken, Task<bool>> processAsync)
        : WorkItemProcessor<TestWorkItem>
    {
        public override Task<bool> ProcessWorkItemAsync(TestWorkItem workItem, CancellationToken cancellationToken) =>
            processAsync(workItem, cancellationToken);
    }
}

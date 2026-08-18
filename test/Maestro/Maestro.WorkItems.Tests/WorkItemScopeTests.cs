// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using AwesomeAssertions;
using Maestro.Common.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Maestro.WorkItems;

namespace Maestro.WorkItem.Tests;

public class WorkItemScopeTests
{
    private ServiceCollection _services = new();
    private WorkItemAdmissionGate _admissionGate = null!;

    [SetUp]
    public void TestSetup()
    {
        _services = new();
        _services.AddOptions();
        _services.AddLogging();
        _services.AddSingleton(new TelemetryClient(new()));

        _admissionGate = new WorkItemAdmissionGate();
        _admissionGate.Open();
    }

    [Test]
    public async Task WorkItemScopeRecordsMetricsTest()
    {
        Mock<ITelemetryScope> metricRecorderScopeMock = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();
        TestWorkItem testWorkItem = new() { Text = string.Empty };
        bool processCalled = false;

        metricRecorderMock
            .Setup(m => m.RecordWorkItemCompletion(testWorkItem.Type, 1, "id"))
            .Returns(metricRecorderScopeMock.Object);

        _services.AddSingleton(metricRecorderMock.Object);
        _services.AddWorkItemProcessor<TestWorkItem, TestWorkItemProcessor>(
            _ => new TestWorkItemProcessor(() => { processCalled = true; return true; }));

        IServiceProvider serviceProvider = _services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(serviceProvider, _admissionGate);
        var started = false;

        await using (WorkItemScope workItemScope = await scopeManager.BeginWorkItemScopeWhenReadyAsync(CancellationToken.None))
        {
            var workItem = JsonSerializer.SerializeToNode(testWorkItem, WorkItemConfiguration.JsonSerializerOptions)!;
            await workItemScope.RunWorkItemAsync(
                workItem,
                attemptNumber: 1,
                maxAttempts: 1,
                metricRecorderScopeMock.Object,
                () => started = true,
                CancellationToken.None);
        }

        metricRecorderScopeMock.Verify(m => m.SetSuccess(), Times.Once);
        processCalled.Should().BeTrue();
        started.Should().BeTrue();
    }

    [Test]
    public async Task WorkItemScopeRecordsMetricsWhenThrowingTest()
    {
        Mock<ITelemetryScope> metricRecorderScopeMock = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();
        TestWorkItem testWorkItem = new() { Text = string.Empty };

        metricRecorderMock
            .Setup(m => m.RecordWorkItemCompletion(testWorkItem.Type, 1, "id"))
            .Returns(metricRecorderScopeMock.Object);

        _services.AddSingleton(metricRecorderMock.Object);
        _services.AddWorkItemProcessor<TestWorkItem, TestWorkItemProcessor>(
            _ => new TestWorkItemProcessor(() => throw new Exception()));

        IServiceProvider serviceProvider = _services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(serviceProvider, _admissionGate);
        var started = false;

        await using (WorkItemScope workItemScope = await scopeManager.BeginWorkItemScopeWhenReadyAsync(CancellationToken.None))
        {
            var workItem = JsonSerializer.SerializeToNode(testWorkItem, WorkItemConfiguration.JsonSerializerOptions)!;
            Func<Task> func =
                async () =>
                await workItemScope.RunWorkItemAsync(
                    workItem,
                    attemptNumber: 1,
                    maxAttempts: 1,
                    metricRecorderScopeMock.Object,
                    () => started = true,
                    CancellationToken.None);
            await func.Should().ThrowAsync<Exception>();
        }

        metricRecorderScopeMock.Verify(m => m.SetSuccess(), Times.Never);
        started.Should().BeTrue();
    }

    [Test]
    public async Task WorkItemScopeReleasesAdmissionWhenDisposedTest()
    {
        _services.AddSingleton(new Mock<ITelemetryRecorder>().Object);
        IServiceProvider serviceProvider = _services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(serviceProvider, _admissionGate);

        WorkItemScope workItemScope = await scopeManager.BeginWorkItemScopeWhenReadyAsync(CancellationToken.None);
        _admissionGate.ActiveAdmissionCount.Should().Be(1);

        await workItemScope.DisposeAsync();
        _admissionGate.ActiveAdmissionCount.Should().Be(0);
    }

    [Test]
    public async Task WorkItemScopeIsNotCreatedWhileAdmissionIsClosedTest()
    {
        _services.AddSingleton(new Mock<ITelemetryRecorder>().Object);
        IServiceProvider serviceProvider = _services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(serviceProvider, _admissionGate);
        _admissionGate.Close();

        Task<WorkItemScope> pendingScope = scopeManager.BeginWorkItemScopeWhenReadyAsync(CancellationToken.None);
        pendingScope.IsCompleted.Should().BeFalse();

        _admissionGate.Open();
        await using WorkItemScope workItemScope = await pendingScope.WaitAsync(TimeSpan.FromSeconds(5));
        _admissionGate.ActiveAdmissionCount.Should().Be(1);
    }

    private class TestWorkItem : WorkItems.WorkItem
    {
        public required string Text { get; set; }
    }

    private class TestWorkItemProcessor : WorkItemProcessor<TestWorkItem>, IWorkItemProcessor
    {
        private readonly Func<bool> _process;

        public TestWorkItemProcessor(Func<bool> process)
        {
            _process = process;
        }

        public override Task<bool> ProcessWorkItemAsync(TestWorkItem workItem, CancellationToken cancellationToken)
            => Task.FromResult(_process());
    }

    [Test]
    public async Task DifferentWorkItemsSameProcessorTest()
    {
        Mock<ITelemetryScope> metricRecorderScopeMock = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();

        metricRecorderMock
            .Setup(m => m.RecordWorkItemCompletion(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>()))
            .Returns(metricRecorderScopeMock.Object);

        _services.AddSingleton(metricRecorderMock.Object);

        string? lastText = null;

        _services.AddWorkItemProcessor<TestWorkItem, TestWorkItemProcessor2>(
            _ => new TestWorkItemProcessor2(s => lastText = s));

        _services.AddWorkItemProcessor<TestWorkItem2, TestWorkItemProcessor2>(
            _ => new TestWorkItemProcessor2(s => lastText = s));

        IServiceProvider serviceProvider = _services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(serviceProvider, _admissionGate);

        bool started = false;

        await using (WorkItemScope workItemScope = await scopeManager.BeginWorkItemScopeWhenReadyAsync(CancellationToken.None))
        {
            var workItem = JsonSerializer.SerializeToNode(new TestWorkItem() { Text = "foo" }, WorkItemConfiguration.JsonSerializerOptions)!;
            await workItemScope.RunWorkItemAsync(
                workItem,
                attemptNumber: 1,
                maxAttempts: 1,
                metricRecorderScopeMock.Object,
                () => started = true,
                CancellationToken.None);
        }

        lastText.Should().Be("foo");
        started.Should().BeTrue();
        started = false;

        await using (WorkItemScope workItemScope = await scopeManager.BeginWorkItemScopeWhenReadyAsync(CancellationToken.None))
        {
            var workItem = JsonSerializer.SerializeToNode(new TestWorkItem2() { Text2 = "bar" }, WorkItemConfiguration.JsonSerializerOptions)!;
            await workItemScope.RunWorkItemAsync(
                workItem,
                attemptNumber: 1,
                maxAttempts: 1,
                metricRecorderScopeMock.Object,
                () => started = true,
                CancellationToken.None);
        }

        lastText.Should().Be("bar");
        started.Should().BeTrue();
    }

    [Test]
    public async Task MultipleProcessorsWithoutFactoryMethodTest()
    {
        Mock<ITelemetryScope> metricRecorderScopeMock = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();

        metricRecorderMock
            .Setup(m => m.RecordWorkItemCompletion(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>()))
            .Returns(metricRecorderScopeMock.Object);

        _services.AddSingleton(metricRecorderMock.Object);

        string? lastText = null;

        _services.AddSingleton<Func<bool>>(() => { lastText = "true"; return true; });
        _services.AddSingleton<Action<string>>(s => lastText = s);
        _services.AddWorkItemProcessor<TestWorkItem, TestWorkItemProcessor>();
        _services.AddWorkItemProcessor<TestWorkItem2, TestWorkItemProcessor2>();

        IServiceProvider serviceProvider = _services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(serviceProvider, _admissionGate);
        var started = false;

        await using (WorkItemScope workItemScope = await scopeManager.BeginWorkItemScopeWhenReadyAsync(CancellationToken.None))
        {
            var workItem = JsonSerializer.SerializeToNode(new TestWorkItem() { Text = "foo" }, WorkItemConfiguration.JsonSerializerOptions)!;
            await workItemScope.RunWorkItemAsync(
                workItem,
                attemptNumber: 1,
                maxAttempts: 1,
                metricRecorderScopeMock.Object,
                () => started = true,
                CancellationToken.None);
        }

        lastText.Should().Be("true");
        started.Should().BeTrue();
        started = false;

        await using (WorkItemScope workItemScope = await scopeManager.BeginWorkItemScopeWhenReadyAsync(CancellationToken.None))
        {
            var workItem = JsonSerializer.SerializeToNode(new TestWorkItem2() { Text2 = "bar" }, WorkItemConfiguration.JsonSerializerOptions)!;
            await workItemScope.RunWorkItemAsync(
                workItem,
                attemptNumber: 1,
                maxAttempts: 1,
                metricRecorderScopeMock.Object,
                () => started = true,
                CancellationToken.None);
        }

        lastText.Should().Be("bar");
        started.Should().BeTrue();
    }

    private class TestWorkItem2 : WorkItems.WorkItem
    {
        public required string Text2 { get; set; }
    }

    private class TestWorkItemProcessor2 : IWorkItemProcessor
    {
        private readonly Action<string> _action;

        public TestWorkItemProcessor2(Action<string> action)
        {
            _action = action;
        }

        public Dictionary<string, object> GetLoggingContextData(WorkItems.WorkItem workItem) => [];
        public string? GetRedisMutexKey(WorkItems.WorkItem workItem) => null;

        public Task<bool> ProcessWorkItemAsync(WorkItems.WorkItem workItem, CancellationToken cancellationToken)
        {
            switch (workItem)
            {
                case TestWorkItem t1:
                    _action(t1.Text);
                    break;
                case TestWorkItem2 t2:
                    _action(t2.Text2);
                    break;
            }

            return Task.FromResult(true);
        }
    }
}

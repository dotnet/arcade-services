// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.WorkItem.Tests;

public class WorkItemScopeTests
{
    private ServiceCollection _services = new();

    [SetUp]
    public void TestSetup()
    {
        _services = new();
        _services.AddOptions();
        _services.AddLogging();
    }

    [Test]
    public async Task WorkItemScopeRecordsMetricsTest()
    {
        Mock<ITelemetryScope> telemetryScope = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();
        TestWorkItem testWorkItem = new() { Text = string.Empty };
        bool processCalled = false;

        metricRecorderMock
            .Setup(m => m.RecordWorkItemCompletion(testWorkItem.Type))
            .Returns(telemetryScope.Object);

        _services.AddSingleton(metricRecorderMock.Object);
        _services.AddWorkItemProcessor<TestWorkItem, TestWorkItemProcessor>(
            _ => new TestWorkItemProcessor(() => { processCalled = true; return true; }));

        IServiceProvider serviceProvider = _services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(false, serviceProvider, Mock.Of<ILogger<WorkItemScopeManager>>());

        using (WorkItemScope workItemScope = scopeManager.BeginWorkItemScopeWhenReady())
        {
            var workItem = JsonSerializer.SerializeToNode(testWorkItem, WorkItemConfiguration.JsonSerializerOptions)!;
            await workItemScope.RunWorkItemAsync(workItem, CancellationToken.None);
        }

        metricRecorderMock.Verify(m => m.RecordWorkItemCompletion(testWorkItem.Type), Times.Once);
        telemetryScope.Verify(m => m.SetSuccess(), Times.Once);
        processCalled.Should().BeTrue();
    }

    [Test]
    public void WorkItemScopeRecordsMetricsWhenThrowingTest()
    {
        Mock<ITelemetryScope> metricRecorderScopeMock = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();
        TestWorkItem testWorkItem = new() { Text = string.Empty };

        metricRecorderMock
            .Setup(m => m.RecordWorkItemCompletion(testWorkItem.Type))
            .Returns(metricRecorderScopeMock.Object);

        _services.AddSingleton(metricRecorderMock.Object);
        _services.AddWorkItemProcessor<TestWorkItem, TestWorkItemProcessor>(
            _ => new TestWorkItemProcessor(() => throw new Exception()));

        IServiceProvider serviceProvider = _services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(false, serviceProvider, Mock.Of<ILogger<WorkItemScopeManager>>());

        using (WorkItemScope workItemScope = scopeManager.BeginWorkItemScopeWhenReady())
        {
            var workItem = JsonSerializer.SerializeToNode(testWorkItem, WorkItemConfiguration.JsonSerializerOptions)!;
            Func<Task> func = async () => await workItemScope.RunWorkItemAsync(workItem, CancellationToken.None);
            func.Should().ThrowAsync<Exception>();
        }

        metricRecorderMock.Verify(m => m.RecordWorkItemCompletion(testWorkItem.Type), Times.Once);
        metricRecorderScopeMock.Verify(m => m.SetSuccess(), Times.Never);
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
        TestWorkItem testWorkItem = new() { Text = string.Empty };

        metricRecorderMock
            .Setup(m => m.RecordWorkItemCompletion(It.IsAny<string>()))
            .Returns(metricRecorderScopeMock.Object);

        _services.AddSingleton(metricRecorderMock.Object);

        string? lastText = null;

        _services.AddWorkItemProcessor<TestWorkItem, TestWorkItemProcessor2>(
            _ => new TestWorkItemProcessor2(s => lastText = s));

        _services.AddWorkItemProcessor<TestWorkItem2, TestWorkItemProcessor2>(
            _ => new TestWorkItemProcessor2(s => lastText = s));

        IServiceProvider serviceProvider = _services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(false, serviceProvider, Mock.Of<ILogger<WorkItemScopeManager>>());

        using (WorkItemScope workItemScope = scopeManager.BeginWorkItemScopeWhenReady())
        {
            var workItem = JsonSerializer.SerializeToNode(new TestWorkItem() { Text = "foo" }, WorkItemConfiguration.JsonSerializerOptions)!;
            await workItemScope.RunWorkItemAsync(workItem, CancellationToken.None);
        }

        lastText.Should().Be("foo");

        using (WorkItemScope workItemScope = scopeManager.BeginWorkItemScopeWhenReady())
        {
            var workItem = JsonSerializer.SerializeToNode(new TestWorkItem2() { Text2 = "bar" }, WorkItemConfiguration.JsonSerializerOptions)!;
            await workItemScope.RunWorkItemAsync(workItem, CancellationToken.None);
        }

        lastText.Should().Be("bar");
    }


    [Test]
    public async Task MultipleProcessorsWithoutFactoryMethodTest()
    {
        Mock<ITelemetryScope> metricRecorderScopeMock = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();
        TestWorkItem testWorkItem = new() { Text = string.Empty };

        metricRecorderMock
            .Setup(m => m.RecordWorkItemCompletion(It.IsAny<string>()))
            .Returns(metricRecorderScopeMock.Object);

        _services.AddSingleton(metricRecorderMock.Object);

        string? lastText = null;

        _services.AddSingleton<Func<bool>>(() => { lastText = "true"; return true; });
        _services.AddSingleton<Action<string>>(s => lastText = s);
        _services.AddWorkItemProcessor<TestWorkItem, TestWorkItemProcessor>();
        _services.AddWorkItemProcessor<TestWorkItem2, TestWorkItemProcessor2>();

        IServiceProvider serviceProvider = _services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(false, serviceProvider, Mock.Of<ILogger<WorkItemScopeManager>>());

        using (WorkItemScope workItemScope = scopeManager.BeginWorkItemScopeWhenReady())
        {
            var workItem = JsonSerializer.SerializeToNode(new TestWorkItem() { Text = "foo" }, WorkItemConfiguration.JsonSerializerOptions)!;
            await workItemScope.RunWorkItemAsync(workItem, CancellationToken.None);
        }

        lastText.Should().Be("true");

        using (WorkItemScope workItemScope = scopeManager.BeginWorkItemScopeWhenReady())
        {
            var workItem = JsonSerializer.SerializeToNode(new TestWorkItem2() { Text2 = "bar" }, WorkItemConfiguration.JsonSerializerOptions)!;
            await workItemScope.RunWorkItemAsync(workItem, CancellationToken.None);
        }

        lastText.Should().Be("bar");
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

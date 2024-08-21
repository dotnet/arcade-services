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
    [Test]
    public async Task WorkItemScopeRecordsMetricsTest()
    {
        IServiceCollection services = new ServiceCollection();

        Mock<ITelemetryScope> telemetryScope = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();
        TestWorkItem testWorkItem = new() { Text = string.Empty };
        bool processCalled = false;

        metricRecorderMock
            .Setup(m => m.RecordWorkItemCompletion(testWorkItem.Type))
            .Returns(telemetryScope.Object);

        services.AddSingleton(metricRecorderMock.Object);
        services.AddWorkItemProcessor<TestWorkItem, TestWorkItemProcessor>();
        services.AddSingleton(new TestWorkItemProcessor(() => processCalled = true));

        IServiceProvider serviceProvider = services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(false, serviceProvider, Mock.Of<ILogger<WorkItemScopeManager>>());

        using (WorkItemScope workItemScope = scopeManager.BeginWorkItemScopeWhenReady())
        {
            await workItemScope.RunWorkItemAsync(JsonSerializer.SerializeToNode(testWorkItem)!, CancellationToken.None);
        }

        metricRecorderMock.Verify(m => m.RecordWorkItemCompletion(testWorkItem.Type), Times.Once);
        telemetryScope.Verify(m => m.SetSuccess(), Times.Once);
        processCalled.Should().BeTrue();
    }

    [Test]
    public void WorkItemScopeRecordsMetricsWhenThrowingTest()
    {
        IServiceCollection services = new ServiceCollection();

        Mock<ITelemetryScope> metricRecorderScopeMock = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();
        TestWorkItem testWorkItem = new() { Text = string.Empty };

        metricRecorderMock
            .Setup(m => m.RecordWorkItemCompletion(testWorkItem.Type))
            .Returns(metricRecorderScopeMock.Object);

        services.AddSingleton(metricRecorderMock.Object);
        services.AddWorkItemProcessor<TestWorkItem, TestWorkItemProcessor>();
        services.AddSingleton(new TestWorkItemProcessor(() => throw new Exception()));

        IServiceProvider serviceProvider = services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(false, serviceProvider, Mock.Of<ILogger<WorkItemScopeManager>>());

        using (WorkItemScope workItemScope = scopeManager.BeginWorkItemScopeWhenReady())
        {
            Func<Task> func = async () => await workItemScope.RunWorkItemAsync(JsonSerializer.SerializeToNode(testWorkItem)!, CancellationToken.None);
            func.Should().ThrowAsync<Exception>();
        }

        metricRecorderMock.Verify(m => m.RecordWorkItemCompletion(testWorkItem.Type), Times.Once);
        metricRecorderScopeMock.Verify(m => m.SetSuccess(), Times.Never);
    }

    private class TestWorkItem : WorkItems.WorkItem
    {
        public required string Text { get; set; }
    }

    private class TestWorkItemProcessor : IWorkItemProcessor<TestWorkItem>
    {
        private readonly Action _process;

        public TestWorkItemProcessor(Action process)
        {
            _process = process;
        }

        public Task<bool> ProcessWorkItemAsync(TestWorkItem workItem, CancellationToken cancellationToken)
        {
            _process();
            return Task.FromResult(true);
        }
    }
}

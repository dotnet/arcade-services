// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.Api.Tests;

public class WorkItemScopeTests
{
    [Test]
    public async Task WorkItemScopeRecordsMetricsTest()
    {
        IServiceCollection services = new ServiceCollection();

        Mock<ITelemetryScope> telemetryScope = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();
        TestWorkItem testWorkItem = new() { Text = string.Empty };

        metricRecorderMock.Setup(m => m.RecordWorkItemCompletion(testWorkItem.Type)).Returns(telemetryScope.Object);

        services.AddSingleton(metricRecorderMock.Object);
        services.AddKeyedSingleton(nameof(TestWorkItem), new Mock<IWorkItemProcessor>().Object);

        IServiceProvider serviceProvider = services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(false, serviceProvider, Mock.Of<ILogger<WorkItemScopeManager>>());

        using (WorkItemScope workItemScope = scopeManager.BeginWorkItemScopeWhenReady())
        {
            workItemScope.InitializeScope(testWorkItem);

            await workItemScope.RunWorkItemAsync(CancellationToken.None);
        }

        metricRecorderMock.Verify(m => m.RecordWorkItemCompletion(testWorkItem.Type), Times.Once);
        telemetryScope.Verify(m => m.SetSuccess(), Times.Once);
    }

    [Test]
    public void WorkItemScopeRecordsMetricsWhenThrowingTest()
    {
        IServiceCollection services = new ServiceCollection();

        Mock<ITelemetryScope> metricRecorderScopeMock = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();
        TestWorkItem textWorkItem = new() { Text = string.Empty };

        metricRecorderMock
            .Setup(m => m.RecordWorkItemCompletion(textWorkItem.Type))
            .Returns(metricRecorderScopeMock.Object);

        services.AddSingleton(metricRecorderMock.Object);

        Mock<IWorkItemProcessor> workItemProcessor = new();
        workItemProcessor.Setup(i => i.ProcessWorkItemAsync(textWorkItem, It.IsAny<CancellationToken>())).Throws<Exception>();
        services.AddKeyedSingleton(nameof(TestWorkItem), workItemProcessor.Object);

        IServiceProvider serviceProvider = services.BuildServiceProvider();

        WorkItemScopeManager scopeManager = new(false, serviceProvider, Mock.Of<ILogger<WorkItemScopeManager>>());

        using (WorkItemScope workItemScope = scopeManager.BeginWorkItemScopeWhenReady())
        {
            workItemScope.InitializeScope(textWorkItem);

            Func<Task> func = async () => await workItemScope.RunWorkItemAsync(CancellationToken.None);
            func.Should().ThrowAsync<Exception>();
        }

        metricRecorderMock.Verify(m => m.RecordWorkItemCompletion(textWorkItem.Type), Times.Once);
        metricRecorderScopeMock.Verify(m => m.SetSuccess(), Times.Never);
    }

    private class TestWorkItem : WorkItem
    {
        public required string Text { get; set; }

        public override string Type => nameof(TestWorkItem);
    }
}

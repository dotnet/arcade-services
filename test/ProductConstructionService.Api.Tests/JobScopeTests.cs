// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProductConstructionService.Api.Telemetry;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Queue.JobRunners;
using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Tests;
public class JobScopeTests
{
    [Test]
    public async Task JobScopeRecordsMetricsTest()
    {
        IServiceCollection services = new ServiceCollection();

        Mock<ITelemetryScope> telemetryScope = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();
        TextJob textJob = new() { Id = Guid.NewGuid(), Text = string.Empty };

        metricRecorderMock.Setup(m => m.RecordJob(textJob)).Returns(telemetryScope.Object);

        services.AddSingleton(metricRecorderMock.Object);
        services.AddKeyedSingleton(nameof(TextJob), new Mock<IJobRunner>().Object);

        IServiceProvider serviceProvider = services.BuildServiceProvider();

        JobProcessorScopeManager scopeManager = new(false, serviceProvider);

        using (JobScope jobScope = scopeManager.BeginJobScopeWhenReady())
        {
            jobScope.InitializeScope(textJob);

            await jobScope.RunJobAsync(CancellationToken.None);
        }

        metricRecorderMock.Verify(m => m.RecordJob(textJob), Times.Once);
        telemetryScope.Verify(m => m.SetSuccess(), Times.Once);
    }

    [Test]
    public void JobScopeRecordsMetricsWhenThrowingTest()
    {
        IServiceCollection services = new ServiceCollection();

        Mock<ITelemetryScope> metricRecorderScopeMock = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();
        TextJob textJob = new() { Id = Guid.NewGuid(), Text = string.Empty };

        metricRecorderMock.Setup(m => m.RecordJob(textJob)).Returns(metricRecorderScopeMock.Object);

        services.AddSingleton(metricRecorderMock.Object);

        Mock<IJobRunner> jobRunnerMock = new();
        jobRunnerMock.Setup(j => j.RunAsync(textJob, It.IsAny<CancellationToken>())).Throws<Exception>();
        services.AddKeyedSingleton(nameof(TextJob), jobRunnerMock.Object);

        IServiceProvider serviceProvider = services.BuildServiceProvider();

        JobProcessorScopeManager scopeManager = new(false, serviceProvider);

        using (JobScope jobScope = scopeManager.BeginJobScopeWhenReady())
        {
            jobScope.InitializeScope(textJob);

            Func<Task> func = async () => await jobScope.RunJobAsync(CancellationToken.None);
            func.Should().ThrowAsync<Exception>();
        }

        metricRecorderMock.Verify(m => m.RecordJob(textJob), Times.Once);
        metricRecorderScopeMock.Verify(m => m.SetSuccess(), Times.Never);
    }
}

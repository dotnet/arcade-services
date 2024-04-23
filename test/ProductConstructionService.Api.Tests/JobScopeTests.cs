// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Queue.JobProcessors;
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
        TextJob textJob = new() { Text = string.Empty };

        metricRecorderMock.Setup(m => m.RecordJobCompletion(textJob.Type)).Returns(telemetryScope.Object);

        services.AddSingleton(metricRecorderMock.Object);
        services.AddKeyedSingleton(nameof(TextJob), new Mock<IJobProcessor>().Object);

        IServiceProvider serviceProvider = services.BuildServiceProvider();

        JobScopeManager scopeManager = new(false, serviceProvider, Mock.Of<ILogger<JobScopeManager>>());

        using (JobScope jobScope = scopeManager.BeginJobScopeWhenReady())
        {
            jobScope.InitializeScope(textJob);

            await jobScope.RunJobAsync(CancellationToken.None);
        }

        metricRecorderMock.Verify(m => m.RecordJobCompletion(textJob.Type), Times.Once);
        telemetryScope.Verify(m => m.SetSuccess(), Times.Once);
    }

    [Test]
    public void JobScopeRecordsMetricsWhenThrowingTest()
    {
        IServiceCollection services = new ServiceCollection();

        Mock<ITelemetryScope> metricRecorderScopeMock = new();
        Mock<ITelemetryRecorder> metricRecorderMock = new();
        TextJob textJob = new() { Text = string.Empty };

        metricRecorderMock.Setup(m => m.RecordJobCompletion(textJob.Type)).Returns(metricRecorderScopeMock.Object);

        services.AddSingleton(metricRecorderMock.Object);

        Mock<IJobProcessor> jobRunnerMock = new();
        jobRunnerMock.Setup(j => j.ProcessJobAsync(textJob, It.IsAny<CancellationToken>())).Throws<Exception>();
        services.AddKeyedSingleton(nameof(TextJob), jobRunnerMock.Object);

        IServiceProvider serviceProvider = services.BuildServiceProvider();

        JobScopeManager scopeManager = new(false, serviceProvider, Mock.Of<ILogger<JobScopeManager>>());

        using (JobScope jobScope = scopeManager.BeginJobScopeWhenReady())
        {
            jobScope.InitializeScope(textJob);

            Func<Task> func = async () => await jobScope.RunJobAsync(CancellationToken.None);
            func.Should().ThrowAsync<Exception>();
        }

        metricRecorderMock.Verify(m => m.RecordJobCompletion(textJob.Type), Times.Once);
        metricRecorderScopeMock.Verify(m => m.SetSuccess(), Times.Never);
    }
}

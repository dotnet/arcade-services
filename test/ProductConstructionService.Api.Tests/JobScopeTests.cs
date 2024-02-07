// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProductConstructionService.Api.Metrics;
using ProductConstructionService.Api.Queue;
using ProductConstructionService.Api.Queue.JobRunners;
using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Tests;
public class JobScopeTests
{
    [Test]
    public async Task JobScopeRecordsTelemetryTest()
    {
        IServiceCollection services = new ServiceCollection();

        Mock<ITelemetryScope> telemetryScopeMock = new();
        Mock<ITelemetryRecorder> telemetryRecorderMock = new();
        TextJob textJob = new() { Id = Guid.NewGuid(), Text = string.Empty };

        telemetryRecorderMock.Setup(m => m.RecordJob(textJob)).Returns(telemetryScopeMock.Object);

        services.AddSingleton(telemetryRecorderMock.Object);
        services.AddKeyedSingleton(nameof(TextJob), new Mock<IJobRunner>().Object);

        IServiceProvider serviceProvider = services.BuildServiceProvider();

        JobsProcessorScopeManager scopeManager = new(true, serviceProvider);

        using (JobScope jobScope = scopeManager.BeginJobScopeWhenReady())
        {
            jobScope.InitializeScope(textJob);

            await jobScope.RunJobAsync(CancellationToken.None);
        }

        telemetryRecorderMock.Verify(m => m.RecordJob(It.IsAny<Job>()), Times.Once);
        telemetryScopeMock.Verify(m => m.SetSuccess(), Times.Once);
    }

    [Test]
    public void JobScopeRecordsTelemetryWhenThrowingTest()
    {
        IServiceCollection services = new ServiceCollection();

        Mock<ITelemetryScope> telemetryScopeMock = new();
        Mock<ITelemetryRecorder> telemetryRecorderMock = new();
        TextJob textJob = new() { Id = Guid.NewGuid(), Text = string.Empty };

        telemetryRecorderMock.Setup(m => m.RecordJob(textJob)).Returns(telemetryScopeMock.Object);

        services.AddSingleton(telemetryRecorderMock.Object);

        Mock<IJobRunner> jobRunnerMock = new();
        jobRunnerMock.Setup(j => j.RunAsync(It.IsAny<Job>(), It.IsAny<CancellationToken>())).Throws<Exception>();
        services.AddKeyedSingleton(nameof(TextJob), jobRunnerMock.Object);

        IServiceProvider serviceProvider = services.BuildServiceProvider();

        JobsProcessorScopeManager scopeManager = new(true, serviceProvider);

        using (JobScope jobScope = scopeManager.BeginJobScopeWhenReady())
        {
            jobScope.InitializeScope(textJob);

            Action action = () => jobScope.RunJobAsync(CancellationToken.None).GetAwaiter().GetResult();
            action.Should().Throw<Exception>();
        }

        telemetryRecorderMock.Verify(m => m.RecordJob(It.IsAny<Job>()), Times.Once);
        telemetryScopeMock.Verify(m => m.SetSuccess(), Times.Never);
    }
}

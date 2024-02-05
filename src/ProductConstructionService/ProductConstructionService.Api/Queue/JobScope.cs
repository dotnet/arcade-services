// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Api.Queue.JobRunners;
using ProductConstructionService.Api.Queue.Jobs;
using System.Diagnostics;
using ProductConstructionService.ServiceDefaults;
using ProductConstructionService.Api.Metrics;

namespace ProductConstructionService.Api.Queue;

public class JobScope(
    JobsProcessorScopeManager status,
    IServiceScope serviceScope) : IDisposable
{
    private readonly IServiceScope _serviceScope = serviceScope;
    private Job? _job = null;

    public void Dispose()
    {
        _serviceScope.Dispose();
        status.JobFinished();
    }

    public void InitializeScope(Job job)
    {
        _job = job;
    }

    public async Task RunJob(CancellationToken cancellationToken)
    {
        if (_job is null)
        {
            throw new Exception("JobScope not initialized! Call InitializeScope before calling RunJob");
        }

        var jobRunner = _serviceScope.ServiceProvider.GetRequiredKeyedService<IJobRunner>(_job.GetType().Name);

        var stopwatch = Stopwatch.StartNew();

        await jobRunner.RunAsync(_job, cancellationToken);
        
        stopwatch.Stop();

        var durationMetricRecorder = _serviceScope.ServiceProvider.GetRequiredService<DurationMetricRecorder>();
        durationMetricRecorder.Record(GetJobDurationHistogramName(_job), stopwatch.ElapsedMilliseconds);
    }

    private static string GetJobDurationHistogramName(Job job) => $"{job.GetType().Name}-{MetricConsts.JobDurationMillisecondsHistogram}";
}

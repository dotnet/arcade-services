// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry;
using ProductConstructionService.Api.Queue.JobRunners;
using ProductConstructionService.Api.Queue.Jobs;
using System.Diagnostics;

namespace ProductConstructionService.Api.Queue;

public class JobScope(JobsProcessorScopeManager status, IServiceScope serviceScope, IMeterFactory meterFactory) : IDisposable
{
    private readonly IServiceScope _serviceScope = serviceScope;
    private readonly IMeterFactory _meterFactory = meterFactory;
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

        Meter meter = _meterFactory.Create("ProductConstructionService.Api.Queue");

        var histogram = meter.CreateHistogram<long>("job-time");

        var stopwatch = Stopwatch.StartNew();
        var jobRunner = _serviceScope.ServiceProvider.GetRequiredKeyedService<IJobRunner>(_job.GetType().Name);
        await jobRunner.RunAsync(_job, cancellationToken);

        stopwatch.Stop();

        histogram.Record(stopwatch.ElapsedMilliseconds);
    }


}

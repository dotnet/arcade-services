// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Api.Queue.JobRunners;
using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Queue;

public class JobScope(JobsProcessorScopeManager status) : IDisposable
{
    private ServiceProvider? _serviceProvider = null;

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        status.JobFinished();
    }

    public void InitializeScope(Job job)
    {
        ServiceCollection services = new();
        services.AddSingleton(job);
        services.AddTransient<JobLogger>();
        services.AddLogging(builder => builder.AddConsole());

        AddJobRunner(services, job);

        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task RunJob(CancellationToken cancellationToken)
    {
        if (_serviceProvider is null)
        {
            throw new Exception("JobScope ServiceProvider not initialized. Call InitializeScope before calling RunJob");
        }

        var jobRunner = _serviceProvider.GetRequiredService<IJobRunner>();
        await jobRunner.RunAsync(cancellationToken);
    }

    private void AddJobRunner(ServiceCollection services, Job job)
    {
        switch (job)
        {
            case TextJob textJob:
                services.AddTransient<IJobRunner, TextJobRunner>();
                break;
            default:
                throw new NotSupportedException($"Unable to process unknown PCS job type: {job.GetType().Name}");
        }
    }
}

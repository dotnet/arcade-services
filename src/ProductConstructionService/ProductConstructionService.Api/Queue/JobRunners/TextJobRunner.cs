// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Queue.JobRunners;

public class TextJobRunner(JobLogger jobLogger, Job job) : IJobRunner
{
    private readonly TextJob _job = (TextJob)job;

    public Task RunAsync(CancellationToken cancellationToken)
    {
        jobLogger.LogInformation("Processed text job. Message: {message}", _job.Text);
        return Task.CompletedTask;
    }
}

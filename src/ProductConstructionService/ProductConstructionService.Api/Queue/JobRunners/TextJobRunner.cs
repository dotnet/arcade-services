// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Queue.JobRunners;

public class TextJobRunner(ILogger<TextJobRunner> logger) : IJobRunner
{
    ILogger<TextJobRunner> _logger = logger;

    public Task RunAsync(Job job, CancellationToken cancellationToken)
    {
        var textJob = (TextJob)job;
        _logger.LogInformation("Processed text job. Message: {message}", textJob.Text);
        return Task.CompletedTask;
    }

}

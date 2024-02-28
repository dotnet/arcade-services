// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Queue.JobProcessors;

public class TextJobProcessor(ILogger<TextJobProcessor> logger) : IJobProcessor
{
    private readonly ILogger<TextJobProcessor> _logger = logger;

    public Task ProcessJobAsync(Job job, CancellationToken cancellationToken)
    {
        var textJob = (TextJob)job;
        _logger.LogInformation("Processed text job. Message: {message}", textJob.Text);
        return Task.CompletedTask;
    }
}

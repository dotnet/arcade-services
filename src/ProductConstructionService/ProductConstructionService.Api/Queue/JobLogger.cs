// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using ProductConstructionService.Api.Queue.Jobs;

namespace ProductConstructionService.Api.Queue;

public class JobLogger(Job job, ILogger<JobLogger> logger) : IDisposable
{
    private readonly ILogger<JobLogger> _logger = logger;
    private readonly Job _job = job;
    private readonly DateTime _created = DateTime.Now;

    public void LogInformation(string message, params object[] args)
    {
        var logMessage = "{jobType} with ID {jobId}: " + message;
        object[] logArgs = [_job.GetType(), _job.Id];
        _logger.LogInformation(logMessage, [.. logArgs, .. args]);
    }

    public void Dispose()
    {
        LogInformation("Took {time} to complete", DateTime.Now - _created);
    }
}

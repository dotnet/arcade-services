// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


namespace ProductConstructionService.Api.Queue;

public class WorkItemProcessor(
    ILogger<WorkItemProcessor> logger,
    string queueName,
    WorkItemProcessorStatus status)
    : BackgroundService
{
    private readonly ILogger<WorkItemProcessor> _logger = logger;
    private readonly string _queueName = queueName;

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting to listen to {queueName}", _queueName);
        while (!stoppingToken.IsCancellationRequested && status.ContinueWorking)
        {
            _logger.LogInformation("I am working!");
            await Task.Delay(5000);
        }
        status.StoppedWorking = true;
        _logger.LogInformation("Done working!");
    }
}

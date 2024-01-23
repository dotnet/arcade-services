// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public class PcsJobProcessorOptions
{
    public PcsJobProcessorOptions(string queueName, int emptyQueueWaitTimeSeconds, int offTimeCheckSeconds)
    {
        QueueName = queueName;
        EmptyQueueWaitTime = TimeSpan.FromSeconds(emptyQueueWaitTimeSeconds);

    }

    public TimeSpan EmptyQueueWaitTime { get; }
    public string QueueName { get; }
    public TimeSpan OffTimeCheck { get; }
}

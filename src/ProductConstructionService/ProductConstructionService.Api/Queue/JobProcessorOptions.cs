// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public class JobProcessorOptions
{
    public const string ConfigurationKey = "JobProcessorOptions";

    public required TimeSpan QueuePollTimeout { get; init; }
    public required string JobQueueName { get; init; }
    public required int MaxJobRetries { get; init; }
    public required TimeSpan QueueMessageInvisibilityTime { get; init; }
}

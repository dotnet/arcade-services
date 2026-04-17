// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.WorkItems;

public class WorkItemConsumerOptions
{
    public const string ConfigurationKey = "WorkItemConsumerOptions";

    public required TimeSpan QueuePollTimeout { get; init; }
    public required int MaxWorkItemRetries { get; init; }
    public required TimeSpan QueueMessageInvisibilityTime { get; init; }
}

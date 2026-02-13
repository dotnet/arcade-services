// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.QueueInsights.Models;

public class QueueInsightsResult<T>
{
    public T Result { get; }

    public IReadOnlyList<string> FailedQueues { get; }

    public QueueInsightsResult(T result, IReadOnlyList<string> failedQueues)
    {
        Result = result;
        FailedQueues = failedQueues;
    }
}

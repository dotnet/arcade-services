// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace Maestro.WorkItems;

public abstract class WorkItem
{
    /// <summary>
    /// Type of the message for easier deserialization.
    /// </summary>
    public string Type => GetType().Name;

    [JsonIgnore]
    public long AttemptNumber { get; set; }

    [JsonIgnore]
    public int MaxAttempts { get; set; }

    /// <summary>
    /// Period of time before the WorkItem becomes visible in the queue.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault | JsonIgnoreCondition.WhenWritingNull)]
    public int? Delay { get; internal set; }

    public bool IsFinalAttempt() => AttemptNumber >= MaxAttempts;

    internal void SetAttemptInfo(long attemptNumber, int maxAttempts)
    {
        AttemptNumber = attemptNumber;
        MaxAttempts = maxAttempts;
    }
}

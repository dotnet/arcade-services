// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace ProductConstructionService.WorkItems;

public abstract class WorkItem
{
    /// <summary>
    /// Type of the message for easier deserialization.
    /// </summary>
    public string Type => GetType().Name;

    /// <summary>
    /// Period of time before the WorkItem becomes visible in the queue.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault | JsonIgnoreCondition.WhenWritingNull)]
    public TimeSpan? Delay { get; internal set; }
}

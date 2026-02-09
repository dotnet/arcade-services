// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.Utilities.Parallel;

public class ParallelismSettings
{
    public int WorkerCount { get; set; }
    public int CrashLoopDelaySeconds { get; set; } = 60;
}

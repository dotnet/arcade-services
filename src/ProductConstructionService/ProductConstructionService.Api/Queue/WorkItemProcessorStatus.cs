// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public class WorkItemProcessorStatus
{
    public bool ContinueWorking { get; set; } = true;
    public bool StoppedWorking { get; set; } = false;
}

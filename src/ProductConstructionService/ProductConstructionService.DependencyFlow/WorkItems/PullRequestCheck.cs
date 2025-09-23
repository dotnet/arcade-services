// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace ProductConstructionService.DependencyFlow.WorkItems;

public class PullRequestCheck : DependencyFlowWorkItem
{
    public required string Url { get; set; }

    public required bool IsCodeFlow { get; set; }
}

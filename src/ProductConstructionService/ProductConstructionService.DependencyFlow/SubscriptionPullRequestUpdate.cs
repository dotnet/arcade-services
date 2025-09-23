// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace ProductConstructionService.DependencyFlow;

public class SubscriptionPullRequestUpdate
{
    public Guid SubscriptionId { get; set; }

    public int BuildId { get; set; }

    public string SourceRepo { get; set; }

    public string CommitSha { get; set; }
}

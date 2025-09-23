// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Maestro.MergePolicies;

public record SubscriptionUpdateSummary
{
    public SubscriptionUpdateSummary(Guid subscriptionId, int buildId, string sourceRepo, string commitSha)
    {
        SubscriptionId = subscriptionId;
        BuildId = buildId;
        SourceRepo = sourceRepo;
        CommitSha = commitSha;
    }

    public Guid SubscriptionId { get; set; }

    public int BuildId { get; set; }

    public string SourceRepo { get; set; }

    public string CommitSha { get; set; }
}

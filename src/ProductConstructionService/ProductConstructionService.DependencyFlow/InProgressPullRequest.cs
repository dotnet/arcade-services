// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.MergePolicies;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

#nullable disable
namespace ProductConstructionService.DependencyFlow;

public class InProgressPullRequest : DependencyFlowWorkItem
{
    public required string Url { get; set; }

    public required string HeadBranch { get; set; }

    public required string SourceSha { get; set; }

    public bool? CoherencyCheckSuccessful { get; set; }

    public List<CoherencyErrorDetails> CoherencyErrors { get; set; }

    public MergePolicyCheckResult MergePolicyResult { get; init; }

    public List<SubscriptionPullRequestUpdate> ContainedSubscriptions { get; set; }

    public List<DependencyUpdateSummary> RequiredUpdates { get; set; }

    public bool? SourceRepoNotified { get; set; }

    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;

    public DateTime LastCheck { get; set; } = DateTime.UtcNow;

    public DateTime? NextCheck { get; set; }

    public InProgressPullRequestState MergeState { get; set; }

    public Dictionary<Guid, int> NextBuildsToProcess { get; set; } = [];

    public CodeFlowDirection CodeFlowDirection { get; set; }

    public bool BlockedFromFutureUpdates { get; set; } = false;
}

public enum InProgressPullRequestState
{
    Mergeable,
    Conflict
}

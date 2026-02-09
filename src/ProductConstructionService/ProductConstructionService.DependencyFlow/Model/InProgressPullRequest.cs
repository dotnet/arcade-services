// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.MergePolicies;
using ProductConstructionService.DependencyFlow.WorkItems;

#nullable disable
namespace ProductConstructionService.DependencyFlow.Model;

public class InProgressPullRequest : DependencyFlowWorkItem
{
    /// <summary>
    /// URL of the pull request.
    /// Note: not the regular URL you'd visit in your browser, but the API URL.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Name of the branch from which changes are proposed.
    /// </summary>
    public required string HeadBranch { get; set; }

    /// <summary>
    /// SHA of the head commit of the PR branch.
    /// </summary>
    public string HeadBranchSha { get; set; }

    /// <summary>
    /// SHA of the commit the update is coming from.
    /// </summary>
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

    public Dictionary<Guid, int> NextBuildsToProcess { get; set; } = [];

    public CodeFlowDirection CodeFlowDirection { get; set; }

    public bool BlockedFromFutureUpdates { get; set; } = false;
}

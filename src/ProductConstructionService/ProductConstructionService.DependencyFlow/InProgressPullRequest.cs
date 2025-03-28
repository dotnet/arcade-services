// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;
using Maestro.MergePolicies;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

#nullable disable
namespace ProductConstructionService.DependencyFlow;

[DataContract]
public class InProgressPullRequest : DependencyFlowWorkItem
{
    [DataMember]
    public required string Url { get; set; }

    [DataMember]
    public required string HeadBranch { get; set; }

    [DataMember]
    public required string SourceSha { get; set; }

    [DataMember]
    public bool? CoherencyCheckSuccessful { get; set; }

    [DataMember]
    public List<CoherencyErrorDetails> CoherencyErrors { get; set; }

    [DataMember]
    public MergePolicyCheckResult MergePolicyResult { get; init; }

    [DataMember]
    public List<SubscriptionPullRequestUpdate> ContainedSubscriptions { get; set; }

    [DataMember]
    public List<DependencyUpdateSummary> RequiredUpdates { get; set; }

    [DataMember]
    public bool? SourceRepoNotified { get; set; }

    [DataMember]
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;

    [DataMember]
    public DateTime LastCheck { get; set; } = DateTime.UtcNow;

    [DataMember]
    public DateTime? NextCheck { get; set; }

    [DataMember]
    public InProgressPullRequestState MergeState { get; set; }

    [DataMember]
    public Dictionary<Guid, int> NextBuildsToProcess { get; set; } = [];

    public CodeFlowDirection CodeFlowDirection { get; set; }
}

public enum InProgressPullRequestState
{
    Mergeable,
    Conflict
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;
using Maestro.MergePolicies;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

#nullable disable
namespace ProductConstructionService.DependencyFlow;

[DataContract]
public class InProgressPullRequest : DependencyFlowWorkItem, IPullRequest
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
    public List<SubscriptionPullRequestUpdate> Contained { get; init; }

    [DataMember]
    public List<DependencyUpdateSummary> RequiredUpdates { get; set; }

    [DataMember]
    public bool? SourceRepoNotified { get; set; }
}

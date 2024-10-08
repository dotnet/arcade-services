// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;
using Maestro.Contracts;

#nullable disable
namespace ProductConstructionService.DependencyFlow.WorkItems;

[DataContract]
public class InProgressPullRequest : ActorWorkItem, IPullRequest
{
    [DataMember]
    public string Url { get; set; }

    [DataMember]
    public bool? CoherencyCheckSuccessful { get; set; }

    [DataMember]
    public List<CoherencyErrorDetails> CoherencyErrors { get; set; }

    [DataMember]
    public MergePolicyCheckResult MergePolicyResult { get; init; }

    [DataMember]
    public List<SubscriptionPullRequestUpdate> ContainedSubscriptions { get; init; }

    [DataMember]
    public List<SubscriptionPullRequestUpdate> Contained { get; init; }

    [DataMember]
    public List<DependencyUpdateSummary> RequiredUpdates { get; set; }

    [DataMember]
    public bool? SourceRepoNotified { get; set; }
}

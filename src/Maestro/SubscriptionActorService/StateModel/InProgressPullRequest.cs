// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Runtime.Serialization;
using Maestro.Contracts;

namespace SubscriptionActorService.StateModel;

[DataContract]
public class InProgressPullRequest : IPullRequest
{
    [DataMember]
    public string Url { get; set; }

    [DataMember]
    public bool? CoherencyCheckSuccessful { get; set; }

    [DataMember]
    public List<CoherencyErrorDetails> CoherencyErrors { get; set; }

    [DataMember]
    public MergePolicyCheckResult MergePolicyResult { get; set; }

    [DataMember]
    public List<SubscriptionPullRequestUpdate> ContainedSubscriptions { get; set; }

    [DataMember]
    public List<SubscriptionPullRequestUpdate> Contained { get; set; }

    [DataMember]
    public List<DependencyUpdateSummary> RequiredUpdates { get; set; }

    [DataMember]
    public bool? SourceRepoNotified { get; set; }
}

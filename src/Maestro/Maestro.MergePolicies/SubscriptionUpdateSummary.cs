// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.Serialization;

namespace Maestro.MergePolicies;
[DataContract]
public record SubscriptionUpdateSummary
{
    public SubscriptionUpdateSummary(Guid subscriptionId, int buildId, string sourceRepo, string commitSha)
    {
        SubscriptionId = subscriptionId;
        BuildId = buildId;
        SourceRepo = sourceRepo;
        CommitSha = commitSha;
    }

    [DataMember]
    public Guid SubscriptionId { get; set; }

    [DataMember]
    public int BuildId { get; set; }

    [DataMember]
    public string SourceRepo { get; set; }

    [DataMember]
    public string CommitSha { get; set; }
}

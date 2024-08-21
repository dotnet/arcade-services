// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;

namespace ProductConstructionService.WorkItems.WorkItemDefinitions;

[JsonDerivedType(typeof(CodeFlowWorkItem), typeDiscriminator: nameof(CodeFlowWorkItem))]
[JsonDerivedType(typeof(UpdateSubscriptionWorkItem), typeDiscriminator: nameof(UpdateSubscriptionWorkItem))]
[JsonDerivedType(typeof(PullRequestRetryWorkItem), typeDiscriminator: nameof(PullRequestRetryWorkItem))]
[JsonDerivedType(typeof(SubscriptionRetryWorkItem), typeDiscriminator: nameof(SubscriptionRetryWorkItem))]
public abstract class WorkItem
{
    public Guid Id { get; } = Guid.NewGuid();
    public abstract string Type { get; }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;
using ProductConstructionService.DependencyFlow.StateModel;

namespace ProductConstructionService.WorkItems.WorkItemDefinitions;

internal class PullRequestReminderWorkItem : WorkItem
{
    public PullRequestReminderWorkItem(string name, PullRequestActorId actorId)
    {
        Name = name;

        if (actorId is NonBatchedPullRequestActorId nonBatched)
        {
            SubscriptionId = nonBatched.SubscriptionId;
        }

        if (actorId is BatchedPullRequestActorId batched)
        {
            Repository = batched.Repository;
            Branch = batched.Branch;
        }
    }

    [JsonConstructor]
    public PullRequestReminderWorkItem(string name, string? repository, string? branch, Guid? subscriptionId)
    {
        Name = name;
        Repository = repository;
        Branch = branch;
        SubscriptionId = subscriptionId;
    }

    public string Name { get; set; }

    public string? Repository { get; set; }

    public string? Branch { get; set; }

    public Guid? SubscriptionId { get; set; }
}

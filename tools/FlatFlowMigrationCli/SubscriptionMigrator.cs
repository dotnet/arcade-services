﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FlatFlowMigrationCli.Operations;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace FlatFlowMigrationCli;

internal interface ISubscriptionMigrator
{
    Task CreateBackflowSubscriptionAsync(string mappingName, string repoUri, string branch, HashSet<string> excludedAssets);
    Task CreateVmrSubscriptionAsync(Subscription outgoing);
    Task DeleteSubscriptionAsync(Subscription incoming);
    Task DisableSubscriptionAsync(Subscription incoming);
}

internal class SubscriptionMigrator : ISubscriptionMigrator
{
    private readonly IProductConstructionServiceApi _pcsClient;
    private readonly ILogger<MigrateRepoOperation> _logger;

    public SubscriptionMigrator(IProductConstructionServiceApi pcsClient, ILogger<MigrateRepoOperation> logger)
    {
        _pcsClient = pcsClient;
        _logger = logger;
    }

    public async Task DisableSubscriptionAsync(Subscription incoming)
    {
        var disabledSubscription = new SubscriptionUpdate
        {
            ChannelName = incoming.Channel.Name,
            SourceRepository = incoming.SourceRepository,
            Enabled = false,
            Policy = incoming.Policy,
            PullRequestFailureNotificationTags = incoming.PullRequestFailureNotificationTags,
        };

        await _pcsClient.Subscriptions.UpdateSubscriptionAsync(incoming.Id, disabledSubscription);
        _logger.LogInformation("Disabled subscription {subscriptionId} {sourceRepository} -> {targetRepository}",
            incoming.Id,
            incoming.SourceRepository,
            incoming.TargetRepository);
    }

    public async Task DeleteSubscriptionAsync(Subscription incoming)
    {
        _logger.LogInformation("Deleting an existing subscription to VMR {subscriptionId}...", incoming.Id);
        await _pcsClient.Subscriptions.DeleteSubscriptionAsync(incoming.Id);
    }

    public async Task CreateVmrSubscriptionAsync(Subscription outgoing)
    {
        _logger.LogInformation("Creating a new VMR subscription for {repoUri}...", outgoing.TargetRepository);

        var newVmrSubscription = new SubscriptionData(
            Constants.VmrChannelName,
            Constants.VmrUri,
            outgoing.TargetRepository,
            outgoing.TargetBranch,
            outgoing.Policy,
            outgoing.PullRequestFailureNotificationTags);

        var newSub = await _pcsClient.Subscriptions.CreateAsync(newVmrSubscription);

        _logger.LogInformation("Created subscription {subscriptionId} for {repoUri} from the VMR", newSub.Id, outgoing.TargetRepository);
    }

    public async Task CreateBackflowSubscriptionAsync(string mappingName, string repoUri, string branch, HashSet<string> excludedAssets)
    {
        // TODO: Verify it does not exist already

        _logger.LogInformation("Creating a backflow subscription for {repoUri}", repoUri);

        var newBackflowSubscription = new SubscriptionData(
            Constants.VmrChannelName,
            Constants.VmrUri,
            repoUri,
            branch,
            new SubscriptionPolicy(batchable: false, UpdateFrequency.EveryBuild)
            {
                MergePolicies = [new MergePolicy() { Name = "Standard" }]
            },
            null)
        {
            SourceEnabled = true,
            SourceDirectory = mappingName,
            ExcludedAssets = [.. excludedAssets],
        };

        var subscription = await _pcsClient.Subscriptions.CreateAsync(newBackflowSubscription);

        _logger.LogInformation("Created a backflow subscription {subscriptionId}", subscription.Id);
    }
}

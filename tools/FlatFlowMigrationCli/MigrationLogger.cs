// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace FlatFlowMigrationCli;

/// <summary>
/// Class that logs the operations instead of performing them.
/// </summary>
internal class MigrationLogger : ISubscriptionMigrator
{
    private readonly ILogger<MigrationLogger> _logger;

    public MigrationLogger(ILogger<MigrationLogger> logger)
    {
        _logger = logger;
    }

    public Task DisableSubscription(Subscription incoming)
    {
        _logger.LogInformation("Would disable a subscription {subscriptionId} {sourceRepository} -> {targetRepository}",
            incoming.Id,
            incoming.SourceRepository,
            incoming.TargetRepository);
        return Task.CompletedTask;
    }

    public Task DeleteSubscription(Subscription incoming)
    {
        _logger.LogInformation("Would delete an existing subscription {subscriptionId}...", incoming.Id);
        return Task.CompletedTask;
    }

    public Task CreateVmrSubscription(Subscription outgoing)
    {
        _logger.LogInformation("Would create subscription VMR -> {repoUri}", outgoing.TargetRepository);
        return Task.CompletedTask;
    }

    public Task CreateBackflowSubscription(string mappingName, string repoUri, string branch, HashSet<string> excludedAssets)
    {
        _logger.LogInformation("Would create a backflow subscription for {repoUri}", repoUri);
        return Task.CompletedTask;
    }
}

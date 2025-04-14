// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace FlatFlowMigrationCli;

internal interface ISubscriptionMigrator
{
    Task CreateBackflowSubscriptionAsync(string mappingName, string repoUri, string branch, HashSet<string> excludedAssets);

    Task CreateForwardFlowSubscriptionAsync(string mappingName, string repoUri, string channelName);

    Task CreateVmrSubscriptionAsync(Subscription subscription);

    Task DeleteSubscriptionAsync(Subscription subscription);

    Task DisableSubscriptionAsync(Subscription subscription);

    Task EnableSubscriptionAsync(Subscription subscription);
}

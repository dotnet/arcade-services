// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;

namespace Maestro.DataProviders;
public interface ISqlBarClient : IBasicBarClient
{
    /// <summary>
    /// Register a subscription update in the database. This is used to track the status of subscription updates.
    /// </summary>
    Task RegisterSubscriptionUpdate(
        Guid subscriptionId,
        string updateMessage);

    #region Configuration Data ingestion

    // Subscriptions
    Task CreateSubscriptionsAsync(IEnumerable<Subscription> subscriptions, bool andSaveContext = true);

    Task UpdateSubscriptionsAsync(IEnumerable<Subscription> subscriptions, bool andSaveContext = true);

    Task DeleteSubscriptionsAsync(IEnumerable<Subscription> subsriptions, bool andSaveContext = true);

    // Channels
    Task CreateChannelsAsync(IEnumerable<Channel> channels, bool andSaveContext = true);

    Task UpdateChannelsAsync(IEnumerable<Channel> channels, bool andSaveContext = true);

    // Default Channels
    Task CreateDefaultChannelsAsync(IEnumerable<DefaultChannel> defaultChannels, bool andSaveContext = true);

    Task UpdateDefaultChannelsAsync(IEnumerable<DefaultChannel> defaultChannels, bool andSaveContext = true);

    // Repository Branch Merge Policies
    Task CreateRepositoryBranchMergePoliciesAsync(IEnumerable<RepositoryBranch> branchMergePolicies, bool andSaveContext = true);

    Task UpdateRepositoryBranchMergePoliciesAsync(IEnumerable<RepositoryBranch> branchMergePolicies, bool andSaveContext = true);









}

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

    Task CreateSubscriptionsAsync(IEnumerable<Subscription> subscriptions, bool andSaveContext = true);

    Task UpdateSubscriptionsAsync(IEnumerable<Subscription> subscription, bool andSaveContext = true);

    Task DeleteSubscriptionsAsync(IEnumerable<Subscription> subscriptions, bool andSaveContext = true);

    Task DeleteNamespaceAsync(string namespaceName, bool andSaveContext = true);

    #endregion Configuration Data ingestion
}

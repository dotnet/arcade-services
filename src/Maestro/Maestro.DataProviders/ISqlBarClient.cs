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
    #region Configuration Data ingestion

    Task CreateSubscriptionsAsync(IEnumerable<Subscription> subscriptions, bool andSaveContext = true);

    Task UpdateSubscriptionsAsync(IEnumerable<Subscription> subscription, bool andSaveContext = true);

    Task DeleteSubscriptionsAsync(IEnumerable<Subscription> subscriptions, bool andSaveContext = true);

    Task DeleteNamespaceAsync(string namespaceName, bool andSaveContext = true);

    #endregion Configuration Data ingestion
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
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
}

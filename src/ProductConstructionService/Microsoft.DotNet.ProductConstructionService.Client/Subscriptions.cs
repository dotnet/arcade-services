// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;

namespace Microsoft.DotNet.ProductConstructionService.Client
{
    /// Any manually applied changes need to live in partial classes outside of the "Generated" folder

    internal partial class Subscriptions : IServiceOperations<ProductConstructionServiceApi>, ISubscriptions
    {
        public async Task<Models.Subscription> TriggerSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await TriggerSubscriptionAsync(default, false, id, cancellationToken);
        }

        public async Task<Models.Subscription> TriggerSubscriptionAsync(Guid id, bool force, CancellationToken cancellationToken = default)
        {
            return await TriggerSubscriptionAsync(default, force, id, cancellationToken);
        }
    }

    public partial interface ISubscriptions
    {
        Task<Models.Subscription> TriggerSubscriptionAsync(
            Guid id,
            CancellationToken cancellationToken = default
        );

        Task<Models.Subscription> TriggerSubscriptionAsync(
            Guid id,
            bool force,
            CancellationToken cancellationToken = default
        );
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Maestro.Client
{
    /// Any manually applied changes need to live in partial classes outside of the "Generated" folder

    internal partial class Subscriptions : IServiceOperations<MaestroApi>, ISubscriptions
    {
        public async Task<Models.Subscription> TriggerSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await TriggerSubscriptionAsync(default(int), id, cancellationToken);
        }
    }

    public partial interface ISubscriptions
    {
        Task<Models.Subscription> TriggerSubscriptionAsync(
            Guid id,
            CancellationToken cancellationToken = default
        );
    }
}

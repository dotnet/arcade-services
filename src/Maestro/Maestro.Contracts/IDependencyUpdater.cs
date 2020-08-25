// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Services.Remoting;

namespace Maestro.Contracts
{
    public interface IDependencyUpdater : IService
    {
        Task StartUpdateDependenciesAsync(int buildId, int channelId);

        Task StartSubscriptionUpdateAsync(Guid subscription);

        Task StartSubscriptionUpdateAsync(Guid subscription, int buildId);

        /// <summary>
        ///     Temporary method for debugging daily update issues
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task CheckDailySubscriptionsAsync(CancellationToken cancellationToken);
    }
}

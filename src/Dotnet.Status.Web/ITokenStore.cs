// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DotNet.Status.Web
{
    public interface ITokenStore
    {
        Task<StoredTokenData> IssueTokenAsync(long userId, DateTimeOffset expiration, string description);
        Task<StoredTokenData> GetTokenAsync(long userId, long tokenId);
        Task<IEnumerable<StoredTokenData>> GetTokensForUserAsync(
            long userId,
            CancellationToken cancellationToken);
    }
}

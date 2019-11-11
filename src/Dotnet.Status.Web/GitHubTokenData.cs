// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace DotNet.Status.Web
{
    internal struct GitHubTokenData
    {
        public GitHubTokenData(long userId, long tokenId, DateTimeOffset expiration, string accessToken)
        {
            UserId = userId;
            TokenId = tokenId;
            Expiration = expiration;
            AccessToken = accessToken;
        }

        public long UserId { get; }
        public long TokenId { get; }
        public DateTimeOffset Expiration { get; }
        public string AccessToken { get; }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace DotNet.Status.Web
{
    public class StoredTokenData
    {
        public StoredTokenData(long userId, long tokenId, DateTimeOffset issued, DateTimeOffset expiration, string description, RevocationStatus revocationStatus)
        {
            UserId = userId;
            TokenId = tokenId;
            Issued = issued;
            Expiration = expiration;
            Description = description;
            RevocationStatus = revocationStatus;
        }

        public long UserId { get; }
        public long TokenId { get; }
        public DateTimeOffset Issued { get; }
        public DateTimeOffset Expiration { get; }
        public string Description { get; }
        public RevocationStatus RevocationStatus { get; }
    }
}

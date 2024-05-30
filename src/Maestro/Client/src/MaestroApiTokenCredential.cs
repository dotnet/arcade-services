// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

#nullable enable
namespace Microsoft.DotNet.Maestro.Client
{
    /// <summary>
    /// Credential used to authenticate to the Maestro API using a specific token.
    /// </summary>
    internal class MaestroApiTokenCredential : TokenCredential
    {
        public MaestroApiTokenCredential(string token)
        {
            Token = token;
        }

        public string Token { get; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new AccessToken(Token, DateTimeOffset.MaxValue);
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new ValueTask<AccessToken>(new AccessToken(Token, DateTimeOffset.MaxValue));
        }
    }
}

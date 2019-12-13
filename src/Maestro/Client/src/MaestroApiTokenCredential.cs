// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Azure.Core;
using System.Threading;
using System.Threading.Tasks;


namespace Microsoft.DotNet.Maestro.Client
{
    public class MaestroApiTokenCredential : TokenCredential
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

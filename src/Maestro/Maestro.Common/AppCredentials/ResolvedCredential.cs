// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;

namespace Maestro.Common.AppCredentials;

/// <summary>
/// Credential with a set token.
/// </summary>
public class ResolvedCredential : TokenCredential
{
    public ResolvedCredential(string token)
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

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;

namespace Maestro.Common.AppCredentials;

/// <summary>
/// Credential with a set token.
/// </summary>
public class ResolvedCredential(string token) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return new AccessToken(token, DateTimeOffset.MaxValue);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return new ValueTask<AccessToken>(new AccessToken(token, DateTimeOffset.MaxValue));
    }
}

/// <summary>
/// Credential that resolves the token on each request.
/// </summary>
public class ResolvingCredential(Func<string> tokenResolver) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext _, CancellationToken __)
    {
        return new AccessToken(tokenResolver(), DateTimeOffset.UtcNow);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext _, CancellationToken __)
    {
        return new ValueTask<AccessToken>(new AccessToken(tokenResolver(), DateTimeOffset.UtcNow));
    }
}

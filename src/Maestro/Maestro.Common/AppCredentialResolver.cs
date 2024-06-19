// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;

namespace Microsoft.DotNet.Maestro.Common;

public static class AppCredentialResolver
{
    /// <summary>
    /// Creates a credential based on parameters provided.
    /// </summary>
    /// <param name="appId">Client ID of the Azure application to request the token for</param>
    /// <param name="disableInteractiveAuth">Whether to include interactive login flows</param>
    /// <param name="token">Token to use directly instead of authenticating.</param>
    /// <param name="federatedToken">Federated token to use for fetching the token. If none supplied, will try other flows.</param>
    /// <param name="managedIdentityId">Managed Identity to use for the auth</param>
    /// <returns>Credential that can be used to call the Maestro API</returns>
    public static TokenCredential CreateCredential(
        string appId,
        bool disableInteractiveAuth,
        string? token = null,
        string? federatedToken = null,
        string? managedIdentityId = null,
        string userScope = ".default")
    {
        // 1. BAR or Entra token that can directly be used to authenticate against a service
        if (!string.IsNullOrEmpty(token))
        {
            return new ResolvedCredential(token!);
        }

        // 2. Federated token that can be used to fetch an app token (for CI scenarios)
        if (!string.IsNullOrEmpty(federatedToken))
        {
            return AppCredential.CreateFederatedCredential(appId, federatedToken!);
        }

        // 3. Managed identity (for server-to-server scenarios - e.g. PCS->Maestro)
        if (!string.IsNullOrEmpty(managedIdentityId))
        {
            return AppCredential.CreateManagedIdentityCredential(appId, managedIdentityId!);
        }

        // 4. Azure CLI authentication setup by the caller (for CI scenarios)
        if (disableInteractiveAuth)
        {
            return AppCredential.CreateNonUserCredential(appId);
        }

        // 5. Interactive login (user-based scenario)
        return AppCredential.CreateUserCredential(appId, userScope);
    }

}

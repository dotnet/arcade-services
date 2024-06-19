// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;

namespace Microsoft.DotNet.Maestro.Common;

public static class AppCredentialResolver
{

    public static TokenCredential CreateTokenCredential(
        string appId,
        bool disableInteractiveAuth,
        string? barApiToken = null,
        string? federatedToken = null,
        string? managedIdentityId = null,
        string userScope = ".default")
    {
        // 1. BAR or Entra token that can directly be used to authenticate against Maestro
        if (!string.IsNullOrEmpty(barApiToken))
        {
            return new ResolvedCredential(barApiToken!);
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

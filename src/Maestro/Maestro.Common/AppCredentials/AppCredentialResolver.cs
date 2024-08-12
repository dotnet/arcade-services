// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;

namespace Maestro.Common.AppCredentials;

public static class AppCredentialResolver
{
    /// <summary>
    /// Creates a credential based on parameters provided.
    /// </summary>
    public static TokenCredential CreateCredential(AppCredentialResolverOptions options)
    {
        // 1. BAR or Entra token that can directly be used to authenticate against a service
        if (!string.IsNullOrEmpty(options.Token))
        {
            return new ResolvedCredential(options.Token!);
        }

        // 2. Managed identity (for server-to-server scenarios - e.g. PCS->Maestro)
        if (!string.IsNullOrEmpty(options.ManagedIdentityId))
        {
            return AppCredential.CreateManagedIdentityCredential(options.AppId, options.ManagedIdentityId!);
        }

        // 3. Azure CLI authentication setup by the caller (for CI scenarios)
        if (options.DisableInteractiveAuth)
        {
            return AppCredential.CreateNonUserCredential(options.AppId);
        }

        // 4. Interactive login (user-based scenario)
        return AppCredential.CreateUserCredential(options.AppId, options.UserScope);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Azure.Core;
using Azure.Identity;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

public static class AzureAuthentication
{
#if DEBUG
    public static TokenCredential GetCliCredential()
        => new ChainedTokenCredential(
            new AzureCliCredential(),
            new DefaultAzureCredential()); // CodeQL [SM05137] This is non-production testing code which is not deployed
#else
    public static TokenCredential GetCliCredential() => new AzureCliCredential();
#endif

    public static TokenCredential GetServiceCredential(bool isDevelopment, string? managedIdentityClientId)
    {
        if (isDevelopment)
        {
            return new DefaultAzureCredential(); // CodeQL [SM05137] This is non-production testing code which is not deployed
        }
        else
        {
            return new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId ??
                throw new ArgumentException($"{nameof(managedIdentityClientId)} can't be null when creating service identity")));
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.Maestro.Common;

public class CredentialResolverOptions
{
    /// <summary>
    /// Client ID of the Azure application to request the token for
    /// </summary>
    public string AppId { get; set; }

    /// <summary>
    /// Whether to include interactive login flows
    /// </summary>
    public bool DisableInteractiveAuth { get; set; }

    /// <summary>
    /// Token to use directly instead of authenticating.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Federated token to use for fetching the token. If none supplied, will try other flows.
    /// </summary>
    public string? FederatedToken { get; set; }

    /// <summary>
    /// Managed Identity to use for the auth
    /// </summary>
    public string? ManagedIdentityId { get; set; }

    /// <summary>
    /// User scope to request the token for (in case of user flows).
    /// </summary>
    public string UserScope { get; set; } = ".default";

    public CredentialResolverOptions(string appId)
    {
        AppId = appId;
    }
}

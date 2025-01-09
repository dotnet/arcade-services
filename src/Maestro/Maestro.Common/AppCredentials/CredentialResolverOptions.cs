// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.Common.AppCredentials;

public class CredentialResolverOptions
{
    /// <summary>
    /// Whether to include interactive login flows
    /// </summary>
    public bool DisableInteractiveAuth { get; set; }

    /// <summary>
    /// Token to use directly instead of authenticating.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Managed Identity to use for the auth
    /// </summary>
    public string? ManagedIdentityId { get; set; }

    /// <summary>
    /// Whether to use local credentials
    /// </summary>
    public bool UseLocalCredentials { get; set; }
}

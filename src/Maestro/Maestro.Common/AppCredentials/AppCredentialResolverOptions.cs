// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.Common.AppCredentials;

public class AppCredentialResolverOptions : CredentialResolverOptions
{
    /// <summary>
    /// Client ID of the Azure application to request the token for
    /// </summary>
    public string AppId { get; }

    /// <summary>
    /// User scope to request the token for (in case of user flows).
    /// </summary>
    public string UserScope { get; set; } = ".default";

    public AppCredentialResolverOptions(string appId)
    {
        AppId = appId;
    }
}

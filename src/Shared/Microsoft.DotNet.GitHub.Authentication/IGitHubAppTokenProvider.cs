// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.GitHub.Authentication
{
    public interface IGitHubAppTokenProvider 
    {
        string GetAppToken();

        /// <summary>
        /// Get an app token using the configuration that corresponds to the logical name specified by <paramref name="name"/>.
        /// </summary>
        string GetAppToken(string name);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.GitHub.Authentication
{
    public class GitHubTokenProviderOptions
    {
        public string PrivateKey { get; set; }
        public int GitHubAppId { get; set; }
    }
}

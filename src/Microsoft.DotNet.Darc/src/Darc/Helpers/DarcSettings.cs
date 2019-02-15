// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.Darc
{
    public class DarcSettings
    {
        public DarcSettings()
        {
        }

        public DarcSettings(GitRepoType gitType, string personalAccessToken)
        {
            GitType = gitType;
            GitRepoPersonalAccessToken = personalAccessToken;
        }

        public string BuildAssetRegistryPassword { get; set; }

        public string GitRepoPersonalAccessToken { get; set; }

        public string BuildAssetRegistryBaseUri { get; set; }

        public GitRepoType GitType { get; set; }

        /// <summary>
        ///     If the git clients need to clone a repository for whatever reason,
        ///     this denotes the root of where the repository should be cloned.
        /// </summary>
        public string TemporaryRepositoryRoot { get; set; }
    }
}

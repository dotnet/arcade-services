// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.DotNet.DarcLib.Helpers;

internal class GitRepoUrlParser
{
    internal static (string RepoName, string Org) GetRepoNameAndOwner(string uri)
    {
        var repoType = GitRepoTypeParser.ParseFromUri(uri);

        if (repoType == GitRepoType.AzureDevOps)
        {
            string[] repoParts = uri.Substring(uri.LastIndexOf('/')).Split('-', 2);

            if (repoParts.Length != 2)
            {
                throw new Exception($"Invalid URI in source manifest. Repo '{uri}' does not end with the expected <GH organization>-<GH repo> format.");
            }

            string org = repoParts[0];
            string repo = repoParts[1];

            // The internal Nuget.Client repo has suffix which needs to be accounted for.
            const string trustedSuffix = "-Trusted";
            if (uri.EndsWith(trustedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                repo = repo.Substring(0, repo.Length - trustedSuffix.Length);
            }

            return new(repo, org);
        }
        else if (repoType == GitRepoType.GitHub)
        {
            string[] repoParts = uri.Substring(Constants.GitHubUrlPrefix.Length).Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (repoParts.Length != 2)
            {
                throw new Exception($"Invalid URI in source manifest. Repo '{uri}' does not end with the expected <GH organization>/<GH repo> format.");
            }

            return new(repoParts[1], repoParts[0]);
        }

        throw new Exception("Unsupported format of repository url " + uri);
    }
}

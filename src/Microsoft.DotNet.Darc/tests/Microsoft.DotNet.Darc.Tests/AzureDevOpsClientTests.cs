// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.DotNet.Darc.Tests
{
    public class AzureDevOpsClientTests
    {
        [Fact]
        private void ParseValidRepoUriTests()
        {
            Dictionary<string, (string account, string project, string repo)> validRepoUris =
                new Dictionary<string, (string account, string project, string repo)>()
            {
                {  "https://dev.azure.com/dnceng/public/_git/foo", ("dnceng", "public", "foo") },
                {  "https://dev.azure.com/dcn2eng/public-s/_git/foo-23bar", ("dcn2eng", "public-s", "foo-23bar") },
                {  "https://dev.azure.com/foo/bar/_git/baz-bop/pullrequest/11?_a=overview", ("foo", "bar", "baz-bop") },
            };

            foreach (var validUri in validRepoUris)
            {
                (string account, string project, string repo) = AzureDevOpsClient.ParseRepoUri(validUri.Key);
                Xunit.Assert.Equal(validUri.Value.account, account);
                Xunit.Assert.Equal(validUri.Value.project, project);
                Xunit.Assert.Equal(validUri.Value.repo, repo);
            }
        }

        [Fact]
        private void ParseInvalidRepoUriTests()
        {
            List<string> invalidRepoUris = new List<string>()
            {
                "https://dnceng.visualstudio.com/public/_git/foo",
                "https://dev.azure.com/dcn-eng/public-s/_git/foo-23bar",
                "https://dnceng@dev.azure.com/dnceng/public/_git/foo-core"
            };

            foreach (var invalidUri in invalidRepoUris)
            {
                Xunit.Assert.Throws<ArgumentException>(() => AzureDevOpsClient.ParseRepoUri(invalidUri));
            }
        }

        [Fact]
        private void ParseValidPullRequestUriTests()
        {
            Dictionary<string, (string account, string project, string repo, int id)> validRepoUris =
                new Dictionary<string, (string account, string project, string repo, int id)>()
            {
                { "https://dev.azure.com/foo/bar/_apis/git/repositories/baz98-bop/pullRequests/112", ("foo", "bar", "baz98-bop", 112) },
                { "https://dev.azure.com/foo/bar/_apis/git/repositories/kidz-bop/pullRequests/1133?_a=files", ("foo", "bar", "kidz-bop", 1133) },
                { "https://dev.azure.com/foo/bar/_apis/git/repositories/baz-bop/pullRequests/141?_a=files&path=%2F.build%2Frestore.yaml", ("foo", "bar", "baz-bop", 141) },
            };

            foreach (var validUri in validRepoUris)
            {
                (string account, string project, string repo, int id) = AzureDevOpsClient.ParsePullRequestUri(validUri.Key);
                Xunit.Assert.Equal(validUri.Value.account, account);
                Xunit.Assert.Equal(validUri.Value.project, project);
                Xunit.Assert.Equal(validUri.Value.repo, repo);
                Xunit.Assert.Equal(validUri.Value.id, id);
            }
        }

        [Fact]
        private void ParseInvalidPullRequestUriTests()
        {
            List<string> invalidPrUrls = new List<string>()
            {
                // Not expecting the non-api form of PR url
                "https://dev.azure.com/foo/bar/_git/baz98-bop/pullRequests/112"
            };

            foreach (var invalidUri in invalidPrUrls)
            {
                Xunit.Assert.Throws<ArgumentException>(() => AzureDevOpsClient.ParsePullRequestUri(invalidUri));
            }
        }
    }
}

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
        [Theory]
        [InlineData("https://dev.azure.com/dnceng/public/_git/foo", "dnceng", "public", "foo")]
        [InlineData("https://borkbork.visualstudio.com/borky/_git/foo2", "borkbork", "borky", "foo2")]
        [InlineData("https://dev.azure.com/dcn2eng/public-s/_git/foo-23bar", "dcn2eng", "public-s", "foo-23bar")]
        [InlineData("https://dev.azure.com/foo/bar/_git/baz-bop", "foo", "bar", "baz-bop")]
        [InlineData("https://dnceng@dev.azure.com/foo/bar/_git/bebop", "foo", "bar", "bebop")]
        [InlineData("https://dnceng.visualstudio.com/int/_git/bebop", "dnceng", "int", "bebop")]
        private void ParseValidRepoUriTests(string inputUri, string expectedAccount, string expectedProject, string expectedRepo)
        {
            (string account, string project, string repo) = AzureDevOpsClient.ParseRepoUri(inputUri);
            Xunit.Assert.Equal(expectedAccount, account);
            Xunit.Assert.Equal(expectedProject, project);
            Xunit.Assert.Equal(expectedRepo, repo);
        }

        [Theory]
        [InlineData("https://dev.azure.com/dcn-eng/public-s/_git/foo-23bar")]
        [InlineData("https://github.com/account/bar")]
        private void ParseInvalidRepoUriTests(string inputUri)
        {
            Xunit.Assert.Throws<ArgumentException>(() => AzureDevOpsClient.ParseRepoUri(inputUri));
        }

        [Theory]
        [InlineData("https://dev.azure.com/foo/bar/_apis/git/repositories/baz98-bop/pullRequests/112", "foo", "bar", "baz98-bop", 112)]
        [InlineData("https://dev.azure.com/foo/bar/_apis/git/repositories/kidz-bop/pullRequests/1133?_a=files", "foo", "bar", "kidz-bop", 1133)]
        [InlineData("https://dev.azure.com/foo/bar/_apis/git/repositories/baz-bop/pullRequests/141?_a=files&path=%2F.build%2Frestore.yaml", "foo", "bar", "baz-bop", 141)]
        private void ParseValidPullRequestUriTests(string inputUri, string expectedAccount,
            string expectedProject, string expectedRepo, int expectedId)
        {
            (string account, string project, string repo, int id) = AzureDevOpsClient.ParsePullRequestUri(inputUri);
            Xunit.Assert.Equal(expectedAccount, account);
            Xunit.Assert.Equal(expectedProject, project);
            Xunit.Assert.Equal(expectedRepo, repo);
            Xunit.Assert.Equal(expectedId, id);
        }

        [Theory]
        [InlineData("https://dev.azure.com/foo/bar/_git/baz98-bop/pullRequests/112")]
        private void ParseInvalidPullRequestUriTests(string inputUri)
        {
            Xunit.Assert.Throws<ArgumentException>(() => AzureDevOpsClient.ParsePullRequestUri(inputUri));
        }

        [Theory]
        [InlineData("https://dev.azure.com/dnceng/public/_git/foo", "https://dev.azure.com/dnceng/public/_git/foo")]
        [InlineData("https://dnceng@dev.azure.com/foo/bar/_git/bebop/pullrequest/11?_a=overview", "https://dev.azure.com/foo/bar/_git/bebop/pullrequest/11?_a=overview")]
        [InlineData("https://dnceng.visualstudio.com/int/_git/bebop", "https://dev.azure.com/dnceng/int/_git/bebop")]
        private void NormalizeRepoUriTests(string inputUri, string expectedUri)
        {
            string normalizedUri = AzureDevOpsClient.NormalizeUrl(inputUri);
            Xunit.Assert.Equal(expectedUri, normalizedUri);
        }
    }
}

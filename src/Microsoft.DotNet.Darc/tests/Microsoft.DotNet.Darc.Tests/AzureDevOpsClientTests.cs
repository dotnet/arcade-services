// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests
{
    [TestFixture]
    public class AzureDevOpsClientTests
    {
        [TestCase("https://dev.azure.com/dnceng/public/_git/foo", "dnceng", "public", "foo")]
        [TestCase("https://borkbork.visualstudio.com/borky/_git/foo2", "borkbork", "borky", "foo2")]
        [TestCase("https://dev.azure.com/dcn2eng/public-s/_git/foo-23bar", "dcn2eng", "public-s", "foo-23bar")]
        [TestCase("https://dev.azure.com/foo/bar/_git/baz-bop", "foo", "bar", "baz-bop")]
        [TestCase("https://dnceng@dev.azure.com/foo/bar/_git/bebop", "foo", "bar", "bebop")]
        [TestCase("https://dnceng.visualstudio.com/int/_git/bebop", "dnceng", "int", "bebop")]
        public void ParseValidRepoUriTests(string inputUri, string expectedAccount, string expectedProject, string expectedRepo)
        {
            (string account, string project, string repo) = AzureDevOpsClient.ParseRepoUri(inputUri);
            account.Should().Be(expectedAccount);
            project.Should().Be(expectedProject);
            repo.Should().Be(expectedRepo);
        }

        [TestCase("https://dev.azure.com/dcn-eng/public-s/_git/foo-23bar")]
        [TestCase("https://github.com/account/bar")]
        public void ParseInvalidRepoUriTests(string inputUri)
        {
            ((Func<object>)(() => AzureDevOpsClient.ParseRepoUri(inputUri))).Should().ThrowExactly<ArgumentException>();
        }

        [TestCase("https://dev.azure.com/foo/bar/_apis/git/repositories/baz98-bop/pullRequests/112", "foo", "bar", "baz98-bop", 112)]
        [TestCase("https://dev.azure.com/foo/bar/_apis/git/repositories/kidz-bop/pullRequests/1133?_a=files", "foo", "bar", "kidz-bop", 1133)]
        [TestCase("https://dev.azure.com/foo/bar/_apis/git/repositories/baz-bop/pullRequests/141?_a=files&path=%2F.build%2Frestore.yaml", "foo", "bar", "baz-bop", 141)]
        public void ParseValidPullRequestUriTests(string inputUri, string expectedAccount,
            string expectedProject, string expectedRepo, int expectedId)
        {
            (string account, string project, string repo, int id) = AzureDevOpsClient.ParsePullRequestUri(inputUri);
            account.Should().Be(expectedAccount);
            project.Should().Be(expectedProject);
            repo.Should().Be(expectedRepo);
            id.Should().Be(expectedId);
        }

        [TestCase("https://dev.azure.com/foo/bar/_git/baz98-bop/pullRequests/112")]
        public void ParseInvalidPullRequestUriTests(string inputUri)
        {
            (((Func<object>)(() => AzureDevOpsClient.ParsePullRequestUri(inputUri)))).Should().ThrowExactly<ArgumentException>();
        }

        [TestCase("https://dev.azure.com/dnceng/public/_git/foo", "https://dev.azure.com/dnceng/public/_git/foo")]
        [TestCase("https://dnceng@dev.azure.com/foo/bar/_git/bebop/pullrequest/11?_a=overview", "https://dev.azure.com/foo/bar/_git/bebop/pullrequest/11?_a=overview")]
        [TestCase("https://dnceng.visualstudio.com/int/_git/bebop", "https://dev.azure.com/dnceng/int/_git/bebop")]
        [TestCase("https://github.com/account/bar", "https://github.com/account/bar")]
        public void NormalizeRepoUriTests(string inputUri, string expectedUri)
        {
            string normalizedUri = AzureDevOpsClient.NormalizeUrl(inputUri);
            normalizedUri.Should().Be(expectedUri);
        }
    }
}

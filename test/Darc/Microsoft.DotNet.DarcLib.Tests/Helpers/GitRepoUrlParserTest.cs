// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.Helpers;

[TestFixture]
public class GitRepoUrlParserTest
{
    [Test]
    public void ParseGitHubUrlTest()
    {
        var gitHubUrl = "https://github.com/org/repo.name";
        var (name, org) = GitRepoUrlUtils.GetRepoNameAndOwner(gitHubUrl);
        name.Should().Be("repo.name");
        org.Should().Be("org");
    }

    [Test]
    public void ParseAzureDevOpsUrlTest()
    {
        var url = "https://dev.azure.com/dnceng/internal/_git/org-repo-name";
        var (name, org) = GitRepoUrlUtils.GetRepoNameAndOwner(url);
        name.Should().Be("repo-name");
        org.Should().Be("org");
    }

    [Test]
    public void ParseAzureDevOpsOtherFormUrlTest()
    {
        var url = "https://dnceng@dev.azure.com/dnceng/someproject/_git/org-repo-name";
        var (name, org) = GitRepoUrlUtils.GetRepoNameAndOwner(url);
        name.Should().Be("repo-name");
        org.Should().Be("org");
    }

    [Test]
    public void ParseVisualStudioUrlTest()
    {
        var url = "https://dnceng.visualstudio.com/internal/_git/org-repo-name";
        var (name, org) = GitRepoUrlUtils.GetRepoNameAndOwner(url);
        name.Should().Be("repo-name");
        org.Should().Be("org");
    }

    [Test]
    public void GitRepoTypeOrdering()
    {
        var repos = new (string Name, string Uri)[]
        {
            ("azdo", "https://dev.azure.com/dnceng/internal/_git/test-repo"),
            ("github1", "https://github.com/dotnet/test-repo"),
            ("github2", "https://github.com/dotnet/test-repo"),
            ("local", new NativePath("/var/test-repo")),
        };

        var sorted = repos
            .OrderBy(r => GitRepoUrlUtils.ParseTypeFromUri(r.Uri), Comparer<GitRepoType>.Create(GitRepoUrlUtils.OrderByLocalPublicOther))
            .Select(r => r.Name)
            .ToArray();

        sorted.Should().ContainInConsecutiveOrder("local", "github1", "github2", "azdo");
    }

    [Test]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/org-repo-name", "https://github.com/org/repo-name")]
    [TestCase("https://github.com/org/repo-name", "https://github.com/org/repo-name")]
    [TestCase("https://dev.azure.com/devdiv/DevDiv/_git/NuGet-NuGet.Client-Trusted", "https://github.com/NuGet/NuGet.Client")]
    public void ConvertInternalUriToPublicTest(string internalUri, string publicUri)
    {
        GitRepoUrlUtils.ConvertInternalUriToPublic(internalUri).Should().Be(publicUri);
    }

    [Test]
    [TestCase("https://github.com/dotnet/aspnetcore", "main", "https://github.com/dotnet/aspnetcore/tree/main")]
    [TestCase("https://github.com/dotnet/aspnetcore", "release/8.0", "https://github.com/dotnet/aspnetcore/tree/release/8.0")]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore", "main", "https://dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore?version=GBmain")]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore", "release/8.0", "https://dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore?version=GBrelease/8.0")]
    public void GetRepoAtBranchUriTest(string repoUri, string branch, string expectedUri)
    {
        GitRepoUrlUtils.GetRepoAtBranchUri(repoUri, branch).Should().Be(expectedUri);
    }

    [Test]
    [TestCase("https://github.com/dotnet/aspnetcore", "abc123", "https://github.com/dotnet/aspnetcore/commit/abc123")]
    [TestCase("https://github.com/dotnet/aspnetcore", "def456789", "https://github.com/dotnet/aspnetcore/commit/def456789")]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore", "abc123", "https://dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore?_a=history&version=GCabc123")]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore", "def456789", "https://dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore?_a=history&version=GCdef456789")]
    public void GetCommitLinkUriTest(string repoUri, string commit, string expectedUri)
    {
        GitRepoUrlUtils.GetCommitUri(repoUri, commit).Should().Be(expectedUri);
    }

    [Test]
    [TestCase("https://api.github.com/repos/dotnet/dotnet/pulls/3205", "https://github.com/dotnet/dotnet/pull/3205")]
    [TestCase("https://api.github.com/repos/dotnet/runtime/pulls/12345", "https://github.com/dotnet/runtime/pull/12345")]
    [TestCase("https://api.github.com/repos/microsoft/CsWinRT/pulls/999", "https://github.com/microsoft/CsWinRT/pull/999")]
    public void TurnApiUrlToWebsite_ConvertsGitHubApiUrlsToWebUrls(string apiUrl, string expectedWebUrl)
    {
        var result = GitRepoUrlUtils.TurnApiUrlToWebsite(apiUrl, null, null);
        result.Should().Be(expectedWebUrl);
    }

    [Test]
    [TestCase("https://dev.azure.com/dnceng/7ea9116e-9fac-403d-b258-b31fcf1bb293/_apis/git/repositories/test-repo-guid/pullRequests/123",
              "https://dev.azure.com/dnceng/internal/_git/test-repo-guid/pullrequest/123")]
    public void TurnApiUrlToWebsite_ConvertsAzureDevOpsApiUrlsToWebUrls(string apiUrl, string expectedWebUrl)
    {
        var result = GitRepoUrlUtils.TurnApiUrlToWebsite(apiUrl, null, null);
        result.Should().Be(expectedWebUrl);
    }

    [Test]
    [TestCase("https://github.com/dotnet/dotnet/pull/3205")]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-wpf/pullrequest/123")]
    [TestCase("not-a-url")]
    public void TurnApiUrlToWebsite_ReturnsOriginalUrlWhenNotApiUrl(string url)
    {
        var result = GitRepoUrlUtils.TurnApiUrlToWebsite(url, null, null);
        result.Should().Be(url);
    }
}

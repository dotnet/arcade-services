// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using NUnit.Framework;
using ProductConstructionService.BarViz.Code.Helpers;

namespace ProductConstructionService.BarViz.Tests;

[TestFixture]
public class BuildHelperTests
{
    [TestCase("https://github.com/dotnet/runtime", "abc1234", "https://github.com/dotnet/runtime/commit/abc1234")]
    [TestCase("https://github.com/dotnet/aspire", "deadbeef", "https://github.com/dotnet/aspire/commit/deadbeef")]
    [TestCase("https://github.com/dotnet/runtime/", "abc1234", "https://github.com/dotnet/runtime/commit/abc1234")]
    public void GetCommitUri_GitHub_ReturnsCommitLink(string repoUrl, string sha, string expected)
    {
        BuildHelper.GetCommitUri(repoUrl, sha).Should().Be(expected);
    }

    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-runtime", "abc1234", "https://dev.azure.com/dnceng/internal/_git/dotnet-runtime?_a=history&version=GCabc1234")]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-aspire", "deadbeef", "https://dev.azure.com/dnceng/internal/_git/dotnet-aspire?_a=history&version=GCdeadbeef")]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-runtime/", "abc1234", "https://dev.azure.com/dnceng/internal/_git/dotnet-runtime?_a=history&version=GCabc1234")]
    public void GetCommitUri_AzureDevOps_ReturnsCommitLink(string repoUrl, string sha, string expected)
    {
        BuildHelper.GetCommitUri(repoUrl, sha).Should().Be(expected);
    }

    [Test]
    public void GetCommitUri_UnknownHost_ReturnsNull()
    {
        BuildHelper.GetCommitUri("https://bitbucket.org/org/repo", "abc1234").Should().BeNull();
    }

    [TestCase("https://github.com/dotnet/runtime", 5, 42, "/channel/5/github:dotnet:runtime/build/42")]
    [TestCase("https://github.com/dotnet/aspire", 9, 100, "/channel/9/github:dotnet:aspire/build/100")]
    public void GetLinkToBuildDetails_GitHub_ReturnsCorrectPath(string repoUrl, int channelId, int buildId, string expected)
    {
        BuildHelper.GetLinkToBuildDetails(repoUrl, channelId, buildId).Should().Be(expected);
    }

    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-runtime", 5, 42, "/channel/5/azdo:dnceng:internal:dotnet-runtime/build/42")]
    [TestCase("https://dev.azure.com/dnceng/internal/_git/dotnet-aspire", 9, 100, "/channel/9/azdo:dnceng:internal:dotnet-aspire/build/100")]
    public void GetLinkToBuildDetails_AzureDevOps_ReturnsCorrectPath(string repoUrl, int channelId, int buildId, string expected)
    {
        BuildHelper.GetLinkToBuildDetails(repoUrl, channelId, buildId).Should().Be(expected);
    }

    [Test]
    public void GetLinkToBuildDetails_UnknownHost_ReturnsNull()
    {
        BuildHelper.GetLinkToBuildDetails("https://bitbucket.org/org/repo", 5, 42).Should().BeNull();
    }
}

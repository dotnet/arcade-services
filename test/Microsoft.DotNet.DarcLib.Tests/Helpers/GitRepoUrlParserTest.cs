// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
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
        var (name, org) = GitRepoUrlParser.GetRepoNameAndOwner(gitHubUrl);
        name.Should().Be("repo.name");
        org.Should().Be("org");
    }

    [Test]
    public void ParseAzureDevOpsUrlTest()
    {
        var url = "https://dev.azure.com/dnceng/internal/_git/org-repo-name";
        var (name, org) = GitRepoUrlParser.GetRepoNameAndOwner(url);
        name.Should().Be("repo-name");
        org.Should().Be("org");
    }

    [Test]
    public void ParseAzureDevOpsOtherFormUrlTest()
    {
        var url = "https://dnceng@dev.azure.com/dnceng/someproject/_git/org-repo-name";
        var (name, org) = GitRepoUrlParser.GetRepoNameAndOwner(url);
        name.Should().Be("repo-name");
        org.Should().Be("org");
    }

    [Test]
    public void ParseVisualStudioUrlTest()
    {
        var url = "https://dnceng.visualstudio.com/internal/_git/org-repo-name";
        var (name, org) = GitRepoUrlParser.GetRepoNameAndOwner(url);
        name.Should().Be("repo-name");
        org.Should().Be("org");
    }
}

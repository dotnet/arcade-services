// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
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
            .OrderBy(r => GitRepoUrlParser.ParseTypeFromUri(r.Uri), Comparer<GitRepoType>.Create(GitRepoUrlParser.OrderByLocalPublicOther))
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
        GitRepoUrlParser.ConvertInternalUriToPublic(internalUri).Should().Be(publicUri);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using static SubscriptionActorService.PullRequestActorImplementation;
using FluentAssertions;
using Maestro.Data.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace SubscriptionActorService.Tests;

[TestFixture]
public class PullRequestDescriptionBuilderTests : PullRequestActorTests
{
    private List<DependencyUpdate> CreateDependencyUpdates(char version)
    {
        return
        [
            new DependencyUpdate
            {
                From = new DependencyDetail
                {
                    Name = $"from dependency name 1{version}",
                    Version = $"1.0.0{version}",
                    CoherentParentDependencyName = $"from parent name 1{version}",
                    Commit = $"{version} commit from 1"
                },
                To = new DependencyDetail
                {
                    Name = $"to dependency name 1{version}",
                    Version = $"1.0.0{version}",
                    CoherentParentDependencyName = $"from parent name 1{version}",
                    RepoUri = "https://amazing_uri.com",
                    Commit = $"{version} commit to 1"
                }
            },
            new DependencyUpdate
            {
                From = new DependencyDetail
                {
                    Name = $"from dependency name 2{version}",
                    Version = $"1.0.0{version}",
                    CoherentParentDependencyName = $"from parent name 2{version}",
                    Commit = $"{version} commit from 2"
                },
                To = new DependencyDetail
                {
                    Name = $"to dependency name 2{version}",
                    Version = $"1.0.0{version}",
                    CoherentParentDependencyName = $"from parent name 2{version}",
                    RepoUri = "https://amazing_uri.com",
                    Commit = $"{version} commit to 2"
                }
            }
        ];
    }

    public static UpdateAssetsParameters CreateUpdateAssetsParameters(bool isCoherencyUpdate, string guid)
    {
        return new UpdateAssetsParameters
        {
            IsCoherencyUpdate = isCoherencyUpdate,
            SourceRepo = "The best repo",
            SubscriptionId = new Guid(guid)
        };
    }

    private static string BuildCorrectPRDescriptionWhenNonCoherencyUpdate(List<DependencyUpdate> deps)
    {
        StringBuilder stringBuilder = new StringBuilder();
        foreach(DependencyUpdate dep in deps)
        {
            stringBuilder.AppendLine($"  - **{dep.To.Name}**: from {dep.From.Version} to {dep.To.Version} (parent: {dep.To.CoherentParentDependencyName})");
        }
        return stringBuilder.ToString();
    }

    private string BuildCorrectPRDescriptionWhenCoherencyUpdate(List<DependencyUpdate> deps, int startingId)
    {
        StringBuilder builder = new StringBuilder();
        List<string> urls = [];
        for(int i = 0; i < deps.Count; i++)
        {
            urls.Add(PullRequestDescriptionBuilder.GetChangesURI(deps[i].To.RepoUri, deps[i].From.Commit, deps[i].To.Commit));
            builder.AppendLine($"  - **{deps[i].To.Name}**: [from {deps[i].From.Version} to {deps[i].To.Version}][{startingId + i}]");
        }
        builder.AppendLine();
        for(int i = 0; i < urls.Count; i++)
        {
            builder.AppendLine($"[{i + startingId}]: {urls[i]}");
        }
        return builder.ToString();
    }

    [Test]
    public void ShouldReturnCalculateCorrectPRDescriptionWhenNonCoherencyUpdate()
    {
        PullRequestDescriptionBuilder pullRequestDescriptionBuilder = new PullRequestDescriptionBuilder(new NullLoggerFactory(), "");
        UpdateAssetsParameters update = CreateUpdateAssetsParameters(true, "11111111-1111-1111-1111-111111111111");
        List<DependencyUpdate> deps = CreateDependencyUpdates('a');

        pullRequestDescriptionBuilder.AppendBuildDescription(update, deps, null, null);

        pullRequestDescriptionBuilder.ToString().Should().Contain(BuildCorrectPRDescriptionWhenNonCoherencyUpdate(deps));
    }

    [Test]
    public void ShouldReturnCalculateCorrectPRDescriptionWhenCoherencyUpdate()
    {
        PullRequestDescriptionBuilder pullRequestDescriptionBuilder = new PullRequestDescriptionBuilder(new NullLoggerFactory(), "");
        UpdateAssetsParameters update1 = CreateUpdateAssetsParameters(false, "11111111-1111-1111-1111-111111111111");
        UpdateAssetsParameters update2 = CreateUpdateAssetsParameters(false, "22222222-2222-2222-2222-222222222222");
        List<DependencyUpdate> deps1 = CreateDependencyUpdates('a');
        List<DependencyUpdate> deps2 = CreateDependencyUpdates('b');
        Build build = GivenANewBuild(true);
            
        pullRequestDescriptionBuilder.AppendBuildDescription(update1, deps1, null, build);
        pullRequestDescriptionBuilder.AppendBuildDescription(update2, deps2, null, build);

        String description = pullRequestDescriptionBuilder.ToString();

        description.Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps1, 1));
        description.Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps2, 3));
    }

    [Test]
    public void ShouldReturnCalculateCorrectPRDescriptionWhenUpdatingExistingPR()
    {
        PullRequestDescriptionBuilder pullRequestDescriptionBuilder = new PullRequestDescriptionBuilder(new NullLoggerFactory(), "");
        UpdateAssetsParameters update1 = CreateUpdateAssetsParameters(false, "11111111-1111-1111-1111-111111111111");
        UpdateAssetsParameters update2 = CreateUpdateAssetsParameters(false, "22222222-2222-2222-2222-222222222222");
        UpdateAssetsParameters update3 = CreateUpdateAssetsParameters(false, "33333333-3333-3333-3333-333333333333");
        List<DependencyUpdate> deps1 = CreateDependencyUpdates('a');
        List<DependencyUpdate> deps2 = CreateDependencyUpdates('b');
        List<DependencyUpdate> deps3 = CreateDependencyUpdates('c');
        Build build = GivenANewBuild(true);

        pullRequestDescriptionBuilder.AppendBuildDescription(update1, deps1, null, build);
        pullRequestDescriptionBuilder.AppendBuildDescription(update2, deps2, null, build);
        pullRequestDescriptionBuilder.AppendBuildDescription(update3, deps3, null, build);

        String description = pullRequestDescriptionBuilder.ToString();

        description.Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps1, 1));
        description.Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps2, 3));
        description.Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps3, 5));

        List<DependencyUpdate> deps22 = CreateDependencyUpdates('d');

        pullRequestDescriptionBuilder.AppendBuildDescription(update2, deps22, null, build);

        description = pullRequestDescriptionBuilder.ToString();

        description.Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps1, 1));
        description.Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps3, 5));
        description.Should().NotContain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps2, 3));
        description.Should().Contain(BuildCorrectPRDescriptionWhenCoherencyUpdate(deps22, 7));
    }

    [TestCaseSource(nameof(RegexTestCases))]
    public void ShouldReturnCorrectMaximumIndex(string str, int expectedResult)
    {
        PullRequestDescriptionBuilder pullRequestDescriptionBuilder = new PullRequestDescriptionBuilder(new NullLoggerFactory(), str);

        pullRequestDescriptionBuilder.GetStartingReferenceId().Should().Be(expectedResult);
    }

    private const string RegexTestString1 = @"
[2]:qqqq
qqqqq
qqqq
[42]:qq
[2q]:qq
[123]
qq[234]:qq
 [345]:qq
";
    private const string RegexTestString2 = "";
    private const string RegexTestString3 = @"
this
string
shouldn't
have
any
matches
";
    private const string RegexTestString4 = @"
[1]:q
[2]:1
[3]:q
[4]:q
";
    private static readonly object[] RegexTestCases =
    [
        new object[] { RegexTestString1, 43},
        new object[] { RegexTestString2, 1},
        new object[] { RegexTestString3, 1},
        new object [] { RegexTestString4, 5},
    ];

    public static void ShouldReturnCorrectChangesURIForGitHub()
    {
        var repoURI = "https://github.com/dotnet/arcade-services";
        var fromSha = "c0b723ce00a751db0dcf93789abd58577bad155a";
        var fromShortSha = fromSha.Substring(0, PullRequestDescriptionBuilder.GitHubComparisonShaLength);
        var toSha = "7455af499329f6a5ed6ef3fc2a5c794ea86933d3";
        var toShortSha = toSha.Substring(0, PullRequestDescriptionBuilder.GitHubComparisonShaLength);

        var changesUrl = PullRequestDescriptionBuilder.GetChangesURI(repoURI, fromSha, toSha);

        var expectedChangeUrl = $"{repoURI}/compare/{fromShortSha}...{toShortSha}";

        changesUrl.Should().Be(expectedChangeUrl);
    }

    public static void ShouldReturnCorrectChangesURIForAzDo()
    {
        var repoURI = "https://dev.azure.com/dnceng/internal/_git/dotnet-arcade-services";
        var fromSha = "689a78855b241afedff9919529d812b1f08f6f76";
        var toSha = "d0adee0f8bfebd04a6fb7ad7f9fb1d53b1ed8ac9";

        var changesUrl = PullRequestDescriptionBuilder.GetChangesURI(repoURI, fromSha, toSha);

        var expectedChangesUrl = $"{repoURI}/branches?baseVersion=GC{fromSha}&targetVersion=GC{toSha}&_a=files";

        changesUrl.Should().Be(expectedChangesUrl);
    }
}

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
        return new List<DependencyUpdate>
        {
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
        };
    }

    public UpdateAssetsParameters CreateUpdateAssetsParameters(bool isCoherencyUpdate, string guid)
    {
        return new UpdateAssetsParameters
        {
            IsCoherencyUpdate = isCoherencyUpdate,
            SourceRepo = "The best repo",
            SubscriptionId = new Guid(guid)
        };
    }

    private string BuildCorrectPRDescriptionWhenNonCoherencyUpdate(List<DependencyUpdate> deps)
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
        List<string> urls = new List<string>();
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

    private const string regexTestString1 = @"
[2]:qqqq
qqqqq
qqqq
[42]:qq
[2q]:qq
[123]
qq[234]:qq
 [345]:qq
";
    private const string regexTestString2 = "";
    private const string regexTestString3 = @"
this
string
shouldn't
have
any
matches
";
    private const string regexTestString4 = @"
[1]:q
[2]:1
[3]:q
[4]:q
";

    static object[] RegexTestCases =
    {
        new object[] { regexTestString1, 43},
        new object[] { regexTestString2, 1},
        new object[] { regexTestString3, 1},
        new object [] { regexTestString4, 5},
    };
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Models.Views;
using BuildInsights.KnownIssues.Models;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Views;

[TestFixture]
public class StepResultViewTest
{
    [Test]
    public void PipelineBuildNameTest()
    {
        StepResultView stepResultView = new StepResultView(new StepResult(), "pipelineBuildNameTest", "", MockMarkdownParameters());
        stepResultView.PipelineBuildName.Should().Be("pipelineBuildNameTest");

    }

    [Test]
    public void ValidateDisplayStepName()
    {
        var stepNameOnly = BuildView("StepName");
        stepNameOnly.DisplayStepName.Should().Be("StepName");
        stepNameOnly.StepHierarchy.Should().BeEmpty();

        List<string> fullStepHierarchyList = ["Build", "OSX", "Test", "Run All Tests"];
        var fullStepHierarchy = BuildView(stepHierarchy: fullStepHierarchyList);
        fullStepHierarchy.DisplayStepName.Should().Be("Build / OSX / Test / Run All Tests");
        fullStepHierarchy.StepHierarchy.Should().BeEquivalentTo(fullStepHierarchyList);

        List<string> stepHierarchyWithDefaultList = ["__default", "OSX", "Test", "Run All Tests"];
        var stepHierarchyWithDefault = BuildView(stepHierarchy: stepHierarchyWithDefaultList);
        stepHierarchyWithDefault.DisplayStepName.Should().Be("OSX / Test / Run All Tests");
        stepHierarchyWithDefault.StepHierarchy.Should().BeEquivalentTo(stepHierarchyWithDefaultList);

        List<string> stepHierarchyWithDuplicateList = ["Build", "Test", "Test", "Run All Tests"];
        var stepHierarchyWithDuplicate = BuildView(stepHierarchy: stepHierarchyWithDuplicateList);
        stepHierarchyWithDuplicate.DisplayStepName.Should().Be("Build / Test / Run All Tests");
        stepHierarchyWithDuplicate.StepHierarchy.Should().BeEquivalentTo(stepHierarchyWithDuplicateList);

        List<string> stepHierarchyWithDefaultAndDuplicateList = ["__default", "Test", "Test", "Run All Tests"];
        var stepHierarchyWithDefaultAndDuplicate = BuildView(stepHierarchy: stepHierarchyWithDefaultAndDuplicateList);
        stepHierarchyWithDefaultAndDuplicate.DisplayStepName.Should().Be("Test / Run All Tests");
        stepHierarchyWithDefaultAndDuplicate.StepHierarchy.Should().BeEquivalentTo(stepHierarchyWithDefaultAndDuplicateList);
    }

    [TestCase("TEST-PULL-REQUEST")]
    [TestCase(null)]
    public void GetReportIssueUrlTest(string pullRequest)
    {
        KnownIssueUrlOptions knownIssueUrlOptions = new KnownIssueUrlOptions()
        {
            Host = "example.host/new/",
            InfrastructureIssueParameters = MockIssueParameters(["infra_label"], "infra_test.template"),
            RepositoryIssueParameters = MockIssueParameters()
        };

        var stepNameOnly = BuildView("StepName", markdownParameters: MockMarkdownParameters(knownIssueUrlOptions, pullRequest));
        stepNameOnly.CreateInfraIssueLink.Should().Contain("StepName").And.Contain("infra_label").And
            .Contain("infra_test.template");
        stepNameOnly.CreateRepoIssueLink.Should().Contain("StepName").And.Contain("StepName");

        if (pullRequest != null)
            stepNameOnly.CreateInfraIssueLink.Should().Contain(pullRequest);
    }

    private StepResultView BuildView(string nameOverride = null, IEnumerable<string> stepHierarchy = null, MarkdownParameters markdownParameters = null)
    {
        return new StepResultView(
            nameOverride,
            null,
            null,
            null,
            ImmutableList<Error>.Empty,
            null,
            stepHierarchy?.ToImmutableList() ?? [],
            ImmutableList<KnownIssue>.Empty,
            markdownParameters ?? MockMarkdownParameters());
    }

    private static IssueParameters MockIssueParameters(List<string> labels = null, string template = null)
    {
        return new IssueParameters
        {
            Labels = labels,
            GithubTemplateName = template
        };
    }

    private static MarkdownParameters MockMarkdownParameters(KnownIssueUrlOptions knownIssueUrlOptions = null, string pullRequest = "TEST-PULL-REQUEST")
    {
        return new MarkdownParameters(new MergedBuildResultAnalysis(), "TEST-REPO", pullRequest,
            new Repository("TEST-REPOSITORY", true), knownIssueUrlOptions);
    }
}

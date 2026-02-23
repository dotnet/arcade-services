// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using AwesomeAssertions;
using BuildInsights.AzureStorage.Cache;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.WorkItems.Models;
using BuildInsights.Data.Models;
using BuildInsights.GitHub.Models;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Octokit;

namespace BuildInsights.BuildAnalysis.Tests.Providers;

public partial class KnownIssueValidationProviderTests
{

    [TestDependencyInjectionSetup]
    public static class TestSetup
    {
        public static void Defaults(IServiceCollection services)
        {
            services.AddLogging(l => l.AddProvider(new NUnitLogger()));

            var mockBuildDataService = new Mock<IBuildDataService>();
            mockBuildDataService.Setup(b => b.GetBuildAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(MockBuild());
            mockBuildDataService.Setup(b => b.GetProjectName(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync("any_project");
            services.AddSingleton(mockBuildDataService.Object);
            services.AddScoped<IContextualStorage, MockContextualStorage>();
        }

        public static Func<IServiceProvider, KnownIssueValidationProvider> Processor(IServiceCollection services)
        {
            services.AddSingleton<KnownIssueValidationProvider>();
            return s => s.GetRequiredService<KnownIssueValidationProvider>();
        }

        public static Func<IServiceProvider, Mock<IBuildAnalysisService>> BuildAnalysisService(
            IServiceCollection collection, BuildResultAnalysis buildResultAnalysis)
        {
            var mockBuildAnalysisService = new Mock<IBuildAnalysisService>();
            mockBuildAnalysisService
                .Setup(b => b.GetBuildResultAnalysisAsync(It.IsAny<BuildReferenceIdentifier>(),
                    It.IsAny<CancellationToken>(), true)).ReturnsAsync(buildResultAnalysis ?? new BuildResultAnalysis());
            collection.AddSingleton(mockBuildAnalysisService.Object);
            return _ => mockBuildAnalysisService;
        }

        public static Func<IServiceProvider, (List<string>, Mock<IGitHubIssuesService>)> GitHubIssueService(
            IServiceCollection collection, Issue issue)
        {
            var issueBody = new List<string>();

            var mockGithubIssueService = new Mock<IGitHubIssuesService>();
            mockGithubIssueService.Setup(g => g.GetIssueAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(issue);

            mockGithubIssueService.Setup(g =>
                    g.UpdateIssueBodyAsync(It.IsAny<string>(), It.IsAny<int>(), Capture.In(issueBody)))
                .Returns(Task.CompletedTask);
            collection.AddSingleton(mockGithubIssueService.Object);

            return _ => (issueBody, mockGithubIssueService);
        }

        public static Func<IServiceProvider, Mock<IKnownIssuesHistoryService>> KnownIssuesHistoryService(
            IServiceCollection collection, List<KnownIssueAnalysis> knownIssueAnalyses)
        {
            var mockKnownIssuesHistory = new Mock<IKnownIssuesHistoryService>();
            mockKnownIssuesHistory.Setup(t => t.GetBuildKnownIssueValidatedRecords(It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(knownIssueAnalyses ?? []);
            collection.AddSingleton(mockKnownIssuesHistory.Object);

            return _ => mockKnownIssuesHistory;
        }
    }

    [Test]
    public async Task BodyMissingKnownIssueTest()
    {
        await using TestData testData = await TestData.Default.WithIssue(MockGithubIssue("")).BuildAsync();
        await testData.Processor.ValidateKnownIssue(TestKnownIssueValidationRequest(), CancellationToken.None);
        testData.GitHubIssueService.Item2.Verify(
            t => t.UpdateIssueBodyAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
    }

    [Test]
    public async Task FailedToCreateKnownIssue()
    {
        string bodyWithKnownIssueIncorrectErrorPattern = @"
```json
{
  ""ErrorPattern"": ""Tests failed: .+\\HostActivation\\.Tests_net8\\.0_x86\\.html""
}
```";

        await using TestData testData = await TestData.Default
            .WithIssue(MockGithubIssue(bodyWithKnownIssueIncorrectErrorPattern)).BuildAsync();
        await testData.Processor.ValidateKnownIssue(TestKnownIssueValidationRequest(), CancellationToken.None);
        List<string> result = testData.GitHubIssueService.Item1;
        KnownIssueValidationResult expectedResult = KnownIssueValidationResult.UnableToCreateKnownIssue("Invalid pattern 'Tests failed: .+\\HostActivation\\.Tests_net8\\.0_x86\\.html' at offset 18. Unrecognized escape sequence \\H.");

        result[0].Should().Contain(expectedResult.Value);
    }


    [Test]
    public async Task KnownIssueNoBuildUrl()
    {
        string bodyWithKnownIssueButNoBuild = @"
```json
{
  ""ErrorMessage"": ""PowerShell exited with code""
}
```";

        await using TestData testData =
            await TestData.Default.WithIssue(MockGithubIssue(bodyWithKnownIssueButNoBuild)).BuildAsync();
        await testData.Processor.ValidateKnownIssue(TestKnownIssueValidationRequest(), CancellationToken.None);
        List<string> result = testData.GitHubIssueService.Item1;
        KnownIssueValidationResult expectedResult = KnownIssueValidationResult.MissingBuild;
        result[0].Should().Contain(expectedResult.Value);
    }


    [Test]
    public async Task KnownIssueFailureMatching()
    {
        string bodyWithKnownIssueAndWithBuild = @"
### Build

https://dev.azure.com/dnceng/internal/_build/results?buildId=123456

```json
{
  ""ErrorMessage"": ""PowerShell exited with code""
}
```";
        var KnownIssueValidationRequest = new KnownIssueValidationRequest
        {
            IssueId = 1234,
            Organization = "ABC",
            Repository = "DEF",
            RepositoryWithOwner = "ABC/DEF"
        };
        var issue = new GitHubIssue((int)KnownIssueValidationRequest.IssueId, body: "issue_body",
            repositoryWithOwner: $"{KnownIssueValidationRequest.Organization}/{KnownIssueValidationRequest.Repository}");
        var knownIssues = new List<KnownIssue> { new(issue, [""], KnownIssueType.Test, new KnownIssueOptions()) };

        await using TestData testData = await TestData.Default
            .WithIssue(MockGithubIssue(bodyWithKnownIssueAndWithBuild, (int)KnownIssueValidationRequest.IssueId))
            .WithBuildResultAnalysis(MockBuildResultAnalysis(knownIssues))
            .BuildAsync();

        await testData.Processor.ValidateKnownIssue(KnownIssueValidationRequest, CancellationToken.None);
        testData.BuildAnalysisService.Verify(t => t.GetBuildResultAnalysisAsync(It.IsAny<BuildReferenceIdentifier>(), It.IsAny<CancellationToken>(), true), Times.Once);
        List<string> result = testData.GitHubIssueService.Item1;
        KnownIssueValidationResult expectedResult = KnownIssueValidationResult.Matched;
        result[0].Should().Contain(expectedResult.Value);
    }


    [Test]
    public async Task KnownIssueFailureNotMatching()
    {
        string bodyWithKnownIssueAndWithBuild = @"
### Build

https://dev.azure.com/dnceng/internal/_build/results?buildId=123456

```json
{
  ""ErrorMessage"": ""PowerShell exited with code""
}
```";

        await using TestData testData = await TestData.Default
            .WithIssue(MockGithubIssue(bodyWithKnownIssueAndWithBuild, 1234))
            .WithBuildResultAnalysis(MockBuildResultAnalysis(new List<KnownIssue>()))
            .BuildAsync();

        await testData.Processor.ValidateKnownIssue(TestKnownIssueValidationRequest(), CancellationToken.None);
        List<string> result = testData.GitHubIssueService.Item1;
        KnownIssueValidationResult expectedResult = KnownIssueValidationResult.NotMatched;
        result[0].Should().Contain(expectedResult.Value);
    }

    [Test]
    public async Task SkippingBecauseItWasAlreadyAnalyzed()
    {
        string bodyWithKnownIssueAndWithBuild = @"
### Build

https://dev.azure.com/dnceng/internal/_build/results?buildId=123456

```json
{
  ""ErrorMessage"": ""PowerShell exited with code""
}
```";

        var knownIssueAnalyzed = new List<KnownIssueAnalysis>
        {
            new() { ErrorMessage = "PowerShell exited with code", BuildId = 123456, IssueId = "1" }
        };

        await using TestData testData = await TestData.Default
            .WithIssue(MockGithubIssue(bodyWithKnownIssueAndWithBuild))
            .WithKnownIssueAnalyses(knownIssueAnalyzed)
            .BuildAsync();

        await testData.Processor.ValidateKnownIssue(TestKnownIssueValidationRequest(), CancellationToken.None);
        testData.GitHubIssueService.Item2.Verify(
            t => t.UpdateIssueBodyAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never());
    }

    [Test]
    public async Task WriteValidationFirstTime()
    {
        string bodyWithValidate = @"

First time doing the validation expecting to preserve previous body information

```json
{
  ""ErrorMessage"": ""PowerShell exited with code""
}
``` ";

        await using TestData testData = await TestData.Default
            .WithIssue(MockGithubIssue(bodyWithValidate))
            .WithBuildResultAnalysis(MockBuildResultAnalysis(new List<KnownIssue>()))
            .BuildAsync();

        await testData.Processor.ValidateKnownIssue(TestKnownIssueValidationRequest(), CancellationToken.None);

        string result = testData.GitHubIssueService.Item1[0];
        result.Should().NotBeEmpty();
        result.Should().Contain(KnownIssueHelper.StartKnownIssueValidationIdentifier, Exactly.Once());
        result.Should().Contain(KnownIssueHelper.EndKnownIssueValidationIdentifier, Exactly.Once());
        result.Should().Contain(bodyWithValidate);
    }


    [Test]
    public async Task UpdateValidation()
    {
        string messageBeforeValidation = @"

First time doing the validation expecting to preserve previous body information

```json
{
  ""ErrorMessage"": ""PowerShell exited with code""
}
```
";

        string bodyValidation = @"
<!-- Known issue validation start -->
 ### Known issue validation

<!-- Known issue validation end -->
";

        string messageAfterValidation = @"Message after validation";

        string body = $"{messageBeforeValidation} {bodyValidation} {messageAfterValidation}";


        await using TestData testData = await TestData.Default
            .WithIssue(MockGithubIssue(body))
            .WithBuildResultAnalysis(MockBuildResultAnalysis(new List<KnownIssue>()))
            .BuildAsync();

        await testData.Processor.ValidateKnownIssue(TestKnownIssueValidationRequest(), CancellationToken.None);

        string result = testData.GitHubIssueService.Item1[0];
        result.Should().NotBeEmpty();
        result.Should().Contain(KnownIssueHelper.StartKnownIssueValidationIdentifier, Exactly.Once());
        result.Should().Contain(KnownIssueHelper.EndKnownIssueValidationIdentifier, Exactly.Once());
        result.Should().Contain(messageBeforeValidation);
        result.Should().Contain(messageAfterValidation);
    }

    [Test]
    public async Task ScenarioWhereValidateIsAlreadyPresent()
    {
        string bodyWithValidate = @"```json
{
  ""ErrorMessage"": ""PowerShell exited with code"",
  ""BuildRetry"": false
}
```

<!-- Known issue validation start -->
 ### Known issue validation
**Build analyzed: :mag_right:** https://dev.azure.com/dnceng-public/public/_build/results?buildId=123456
**Result validation: :white_check_mark:** Matched
**Status of validation: :white_check_mark:**   Done
<!-- Known issue validation end -->";

        await using TestData testData = await TestData.Default
            .WithIssue(MockGithubIssue(bodyWithValidate))
            .WithBuildResultAnalysis(MockBuildResultAnalysis(new List<KnownIssue>()))
            .BuildAsync();

        await testData.Processor.ValidateKnownIssue(TestKnownIssueValidationRequest(), CancellationToken.None);

        string result = testData.GitHubIssueService.Item1[0];
        result.Should().NotBeEmpty();
        result.Should().Contain(KnownIssueHelper.StartKnownIssueValidationIdentifier, Exactly.Once());
        result.Should().Contain(KnownIssueHelper.EndKnownIssueValidationIdentifier, Exactly.Once());
    }


    [Test]
    public async Task SelectBuildThatIsInsideValidationSection()
    {
        string bodyWithValidate = @"
### Build

https://dev.azure.com/dnceng-public/public/_build/results?buildId=123456

```json
{
  ""ErrorMessage"": ""PowerShell exited with code"",
  ""BuildRetry"": false
}
```

<!-- Known issue validation start -->
 ### Known issue validation
**Build analyzed: :mag_right:** https://dev.azure.com/dnceng-public/public/_build/results?buildId=123456
**Result validation: :white_check_mark:** Matched
**Status of validation: :white_check_mark:**   Done
<!-- Known issue validation end -->";

        await using TestData testData = await TestData.Default
            .BuildAsync();

        BuildFromGitHubIssue result = await testData.Processor.GetBuildFromBody(bodyWithValidate);

        result.Id.Should().Be(123456);
        result.OrganizationId.Should().Be("dnceng-public");
        result.ProjectId.Should().Be("public");
    }


    [Test]
    public async Task ScenarioWithReportedPresent()
    {
        string bodyWithReport = @"
```json
{
  ""ErrorMessage"": ""PowerShell exited with code""
}
```

<!--Known issue error report start -->
### Report
|Build|Definition|Step Name|Console log|Pull Request|
|---|---|---|---|---|
|[238967](https://dev.azure.com/dnceng-public/public/_build/results?buildId=238967)|maestro-auth-test/build-result-analysis-test|Run Tests (Windows)|[Log](https://dev.azure.com/dnceng-public/cbb18261-c48f-4abb-8651-8cdcb5474649/_apis/build/builds/238967/logs/17)|maestro-auth-test/build-result-analysis-test#776|
#### Summary
|24-Hour Hit Count|7-Day Hit Count|1-Month Count|
|---|---|---|
|1|2|3|
<!--Known issue error report end -->";

        await using TestData testData = await TestData.Default.WithIssue(MockGithubIssue(bodyWithReport)).BuildAsync();
        await testData.Processor.ValidateKnownIssue(TestKnownIssueValidationRequest(), CancellationToken.None);
        List<string> result = testData.GitHubIssueService.Item1;
        KnownIssueValidationResult expectedResult = KnownIssueValidationResult.MissingBuild;
        result[0].Should().Contain(expectedResult.Value);
    }


    [TestCase("https://dev.azure.com/dnceng-public/public/_build/results?buildId=123456", "dnceng-public", "public",
        123456)]
    [TestCase("Build: https://dev.azure.com/dnceng-public/public/_build/results?buildId=123456", "dnceng-public",
        "public", 123456)]
    [TestCase(
        "https://dev.azure.com/dnceng/internal/_build/results?buildId=654321&view=logs&j=3dc8fd7e-4368-5a92-293e-d53cefc8c4b3&t=a6142ecf-2061-5c22-a7cf-4a3322e4561a",
        "dnceng", "internal", 654321)]
    [TestCase(
        "https://dev.azure.com/dnceng/internal/_build/results?buildId=654321&view=ms.vss-test-web.build-test-results-tab&runId=51548624&resultId=100543&paneView=debug",
        "dnceng", "internal", 654321)]
    [TestCase("https://dev.azure.com/dnceng-public/cbb18261-c48f-4abb-8651-8cdcb5474649/_build/results?buildId=310510", "dnceng-public", "any_project", 310510)]
    public async Task GetUrl(string body, string organizationId, string projectId, int buildId)
    {
        await using TestData testData = await TestData.Default.BuildAsync();
        BuildFromGitHubIssue build = await testData.Processor.GetBuildFromBody(body);
        build.OrganizationId.Should().Be(organizationId);
        build.ProjectId.Should().Be(projectId);
        build.Id.Should().Be(buildId);
    }


    private static Build MockBuild()
    {
        var build = new Build();
        return build;
    }

    private Issue MockGithubIssue(string body, int issueId = 1)
    {
        return new Issue(default, default, default, default, issueId, ItemState.Open, default, body, default, default,
            default, default, default, default, 1, default, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue,
            DateTimeOffset.MaxValue, 1, default, default, default, default, default, default);
    }

    private KnownIssueValidationRequest TestKnownIssueValidationRequest()
    {
        return new KnownIssueValidationRequest
        {
            Organization = "TEST_ORGANIZATION",
            Repository = "TEST_REPOSITORY",
            IssueId = 1
        };
    }

    public BuildResultAnalysis MockBuildResultAnalysis(List<KnownIssue> buildKnownIssue)
    {
        return new BuildResultAnalysis
        {
            PipelineName = "PIPELINE_TEST",
            BuildId = 123456,
            BuildNumber = "2021.23.34",
            BuildStatus = BuildStatus.Failed,
            TestResults = [],
            BuildStepsResult = [new() { KnownIssues = buildKnownIssue.ToImmutableList() }],
            TotalTestFailures = 2,
            TestKnownIssuesAnalysis = new TestKnownIssuesAnalysis(true, [])
        };
    }


    internal class MockContextualStorage : BaseContextualStorage
    {
        readonly Dictionary<string, byte[]> _data = [];
        protected override async Task PutAsync(string root, string name, Stream data, CancellationToken cancellationToken)
        {
            using MemoryStream stream = new MemoryStream();
            await data.CopyToAsync(stream, cancellationToken);
            _data[name] = stream.ToArray();
        }

        protected override Task<Stream> TryGetAsync(string root, string name, CancellationToken cancellationToken)
        {
            if (_data.TryGetValue(name, out byte[] mem))
            {
                return Task.FromResult<Stream>(new MemoryStream(mem, false));
            }

            return Task.FromResult<Stream>(null);
        }

        public new string PathContext => base.PathContext;
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.WorkItems.Models;
using BuildInsights.Data.Models;
using BuildInsights.GitHub;
using BuildInsights.KnownIssues;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.Extensions.Logging;
using ProductConstructionService.WorkItems;

namespace BuildInsights.BuildAnalysis.WorkItems.Processors;

public class CheckRunConclusionUpdateProcessor : WorkItemProcessor<CheckRunConclusionUpdateEvent>
{
    private readonly IGitHubChecksService _gitHubChecksService;
    private readonly IMarkdownGenerator _markdownGenerator;
    private readonly IBuildProcessingStatusService _processingStatusService;
    private readonly IGitHubIssuesService _gitHubIssuesService;
    private readonly GitHubTokenProviderOptions _gitHubTokenProviderOptions;
    private readonly ILogger<CheckRunConclusionUpdateProcessor> _logger;

    public CheckRunConclusionUpdateProcessor(
        IGitHubChecksService gitHubChecksService,
        IMarkdownGenerator markdownGenerator,
        IBuildProcessingStatusService processingStatusService,
        IGitHubIssuesService gitHubIssuesService,
        GitHubTokenProviderOptions gitHubTokenProviderOptions,
        ILogger<CheckRunConclusionUpdateProcessor> logger)
    {
        _gitHubChecksService = gitHubChecksService;
        _markdownGenerator = markdownGenerator;
        _processingStatusService = processingStatusService;
        _gitHubIssuesService = gitHubIssuesService;
        _gitHubTokenProviderOptions = gitHubTokenProviderOptions;
        _logger = logger;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        CheckRunConclusionUpdateEvent workItem,
        CancellationToken cancellationToken)
    {
        GitHub.Models.CheckRun buildAnalysisCheckRun = await _gitHubChecksService.GetCheckRunAsyncForApp(
            workItem.Repository,
            workItem.HeadSha,
            _gitHubTokenProviderOptions.GitHubAppId,
            "Build Insights"); // TODO - Constant

        if (buildAnalysisCheckRun == null)
        {
            _logger.LogInformation("Unable to find Build Analysis check run of commit {commit} on repository {repository}",
                workItem.HeadSha, workItem.Repository);
            return false;
        }

        if (string.IsNullOrWhiteSpace(workItem.Justification))
        {
            string justificationMissingMessage = $"Unable to override the build analysis check run because no reason was provided. Please provide a reason. Post a request in the following format: {BuildAnalysisEscapeMechanismHelper.ChangeToGreenCommand} <*reason*>";
            await _gitHubIssuesService.AddCommentToIssueAsync(workItem.Repository, workItem.IssueNumber, justificationMissingMessage);
            return false;
        }

        var buildAnalysisUpdateOverrideResultView = new BuildAnalysisUpdateOverridenResult(
            workItem.Justification,
            buildAnalysisCheckRun.Conclusion.ToString(),
            workItem.CheckResultString,
            buildAnalysisCheckRun.Body);

        _logger.LogInformation("Starting update of build analysis check run from {prevStatus} to {newStatus}", buildAnalysisCheckRun.Status.ToString(), workItem.CheckResultString);
        string markdownAnalysisOverridenResult = _markdownGenerator.GenerateMarkdown(buildAnalysisUpdateOverrideResultView);
        await _gitHubChecksService.UpdateCheckRunConclusion(buildAnalysisCheckRun, workItem.Repository, markdownAnalysisOverridenResult, workItem.GetCheckConclusion());
        _logger.LogInformation("Build analysis check run updated");

        _logger.LogInformation("Saving builds that were part of the check run analyzed");

        List<GitHub.Models.CheckRun> buildCheckRuns = [..await _gitHubChecksService.GetBuildCheckRunsAsync(workItem.Repository, workItem.HeadSha)];
        List<(string repository, int buildId)> buildCheckRunsToUpdate = [..buildCheckRuns.Select(t => (workItem.Repository, t.AzureDevOpsBuildId))];

        await _processingStatusService.SaveBuildAnalysisProcessingStatus(
            buildCheckRunsToUpdate,
            BuildProcessingStatus.ConclusionOverridenByUser);

        _logger.LogInformation("Builds saved as overridden: {buildsOverridden}", buildCheckRunsToUpdate.Select(t => t.buildId));
        return true;
    }
}

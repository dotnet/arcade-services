// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BuildInsights.AzureStorage.Cache;
using BuildInsights.BuildAnalysis;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.WorkItems;
using BuildInsights.BuildAnalysis.WorkItems.Models;
using BuildInsights.Data.Models;
using BuildInsights.GitHub;
using BuildInsights.GitHub.Models;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using BuildInsights.KnownIssues.WorkItems;
using BuildInsights.QueueInsights;
using BuildInsights.Utilities.AzureDevOps.Models;
using Kusto.Data.Exceptions;
using Microsoft.ApplicationInsights;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

#nullable disable
namespace BuildInsights.BuildResultProcessor;

public class AnalysisProcessor : IQueueMessageHandler
{
    private readonly IMergedBuildAnalysisService _buildAnalysis;
    private readonly IBuildAnalysisService _buildAnalysisService;
    private readonly IMarkdownGenerator _markdownGenerator;
    private readonly IGitHubChecksService _gitHubChecksService;
    private readonly IBuildDataService _build;
    private readonly ILogger<AnalysisProcessor> _logger;
    private readonly TelemetryClient _telemetry;
    private readonly IContextualStorage _contextualStorage;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ISystemClock _clock;
    private readonly KnownIssueUrlOptions _knownIssueUrlOptions;
    private readonly InternalProject _internalProject;
    private readonly IPullRequestService _pullRequestProcessor;
    private readonly OperationManager _operations;
    private readonly IBuildAnalysisHistoryService _tableService;
    private readonly IBuildProcessingStatusService _processingStatusService;
    private readonly IRelatedBuildService _relatedBuildService;
    private readonly IQueueInsightsService _queueInsightsService;
    private readonly IGitHubIssuesService _githubIssuesService;
    private readonly IAzDoToGitHubRepositoryService _azDoToGitHubRepositoryService;
    private readonly IKnownIssueValidationService _knownIssueValidationService;
    private readonly IPipelineRequestedService _pipelineRequestedService;
    private readonly IBuildAnalysisRepositoryConfigurationService _buildAnalysisConfiguration;
    private readonly GitHubTokenProviderOptions _gitHubTokenProviderOptions;
    private const string CheckRunName = "Build Analysis";
    private const string CheckRunOutputName = ".NET Result Analysis";
    private const int ErrorMessageLimitLength = 1000;
    private const int TestKnownIssueDisplayLimit = 50;

    public AnalysisProcessor(
        IMergedBuildAnalysisService buildAnalysis,
        IBuildAnalysisService buildAnalysisService,
        IMarkdownGenerator markdownGenerator,
        IGitHubChecksService gitHubChecksService,
        IBuildDataService build,
        TelemetryClient telemetry,
        IContextualStorage contextualStorage,
        IDistributedLockService distributedLockService,
        ISystemClock clock,
        OperationManager operations,
        IPullRequestService pullRequestProcessor,
        IOptions<KnownIssueUrlOptions> knownIssueUrlOptions,
        IOptions<InternalProject> internalProject,
        IBuildAnalysisHistoryService tableService,
        IBuildProcessingStatusService processingStatusService,
        ILogger<AnalysisProcessor> logger,
        IRelatedBuildService relatedBuildService,
        IQueueInsightsService queueInsightsService,
        IGitHubIssuesService gitHubIssuesService,
        IKnownIssueValidationService knownIssueValidationService,
        IAzDoToGitHubRepositoryService azDoToGitHubRepositoryService,
        IPipelineRequestedService pipelineRequestedService,
        IBuildAnalysisRepositoryConfigurationService buildAnalysisConfiguration,
        IOptions<GitHubTokenProviderOptions> gitHubTokenProviderOptions)
    {
        _buildAnalysis = buildAnalysis;
        _buildAnalysisService = buildAnalysisService;
        _markdownGenerator = markdownGenerator;
        _gitHubChecksService = gitHubChecksService;
        _build = build;
        _logger = logger;
        _relatedBuildService = relatedBuildService;
        _telemetry = telemetry;
        _contextualStorage = contextualStorage;
        _distributedLockService = distributedLockService;
        _clock = clock;
        _operations = operations;
        _knownIssueUrlOptions = knownIssueUrlOptions.Value;
        _internalProject = internalProject.Value;
        _tableService = tableService;
        _processingStatusService = processingStatusService;
        _relatedBuildService = relatedBuildService;
        _queueInsightsService = queueInsightsService;
        _pullRequestProcessor = pullRequestProcessor;
        _githubIssuesService = gitHubIssuesService;
        _azDoToGitHubRepositoryService = azDoToGitHubRepositoryService;
        _knownIssueValidationService = knownIssueValidationService;
        _pipelineRequestedService = pipelineRequestedService;
        _buildAnalysisConfiguration = buildAnalysisConfiguration;
        _gitHubTokenProviderOptions = gitHubTokenProviderOptions.Value;
    }

    public async Task HandleMessageAsync(IQueuedWork message, bool isLastAttempt, CancellationToken cancellationToken)
    {
        // DONE
        //string messageString = await message.GetStringAsync();
        //if (_pullRequestProcessor.IsPullRequestMessage(messageString, out PullRequestData pullRequestData))
        //{
        //    await _pullRequestProcessor.ProcessPullRequestMessage(pullRequestData);

        //    //The message was processed as a pull request message and there is nothing left to process
        //    return;
        //}

        string orgId;
        string projectId;
        int buildId;
        // Convert to the AzDO Event Base class
        AzureDevOpsEventBase baseMessage = JsonSerializer.Deserialize<AzureDevOpsEventBase>(messageString);

        // Determine what kind of event it is
        switch (baseMessage.EventType)
        {
            // DONE
            //case "build.complete":
            //    CompletedBuildMessage buildMessage = JsonSerializer.Deserialize<CompletedBuildMessage>(messageString);
            //    orgId = buildMessage.Resource.OrgId;
            //    projectId = buildMessage.ResourceContainers.Project.Id;
            //    buildId = buildMessage.Resource.Id;
            //    break;
            //case "knownissue.reprocessing":
            //    BuildAnalysisRequestWorkItem knownIssueReprocessBuildMessage = JsonSerializer.Deserialize<BuildAnalysisRequestWorkItem>(messageString);
            //    orgId = knownIssueReprocessBuildMessage.OrganizationId;
            //    projectId = knownIssueReprocessBuildMessage.ProjectId;
            //    buildId = knownIssueReprocessBuildMessage.BuildId;
            //    break;
            //case "ms.vss-pipelines.run-state-changed-event":
            //case "ms.vss-pipelines.stage-state-changed-event":
            //    StartedBuildMessage startedBuildMessage = JsonSerializer.Deserialize<StartedBuildMessage>(messageString);
            //    orgId = startedBuildMessage.GetOrgId();
            //    projectId = startedBuildMessage.GetProjectId();
            //    buildId = startedBuildMessage.Resource.Id;
            //    break;
            //case "checkrun.rerun":
            //    RerunCheckRunAnalysisMessage rerunMessage = JsonSerializer.Deserialize<RerunCheckRunAnalysisMessage>(messageString);
            //    Build relatedBuild = await _relatedBuildService.GetRelatedBuildFromCheckRun(rerunMessage?.Repository, rerunMessage?.HeadSha);
            //    if (relatedBuild == null)
            //    {
            //        _logger.LogInformation("No authorized related build found for rerun of commit {commit} on repository {repository}", rerunMessage?.HeadSha, rerunMessage?.Repository);
            //        return;
            //    }
            //    orgId = relatedBuild.OrganizationName;
            //    projectId = relatedBuild.ProjectId;
            //    buildId = relatedBuild.Id;
            //    break;

            case "knownissue.validate":
                KnownIssueValidationMessage knownIssueValidationMessage = JsonSerializer.Deserialize<KnownIssueValidationMessage>(messageString);
                await ValidateKnownIssueMessage(knownIssueValidationMessage, cancellationToken);
                return;

            case "checkrun.conclusion-update":
                CheckRunConclusionUpdateMessage checkRunConclusionUpdateMessage = JsonSerializer.Deserialize<CheckRunConclusionUpdateMessage>(messageString);
                await UpdateBuildAnalysisCheckRun(checkRunConclusionUpdateMessage);
                return;

            default:
                _logger.LogError("Unexpected event type from Azure DevOps {eventType}, aborting", baseMessage.EventType);
                return;
        }

        _logger.LogInformation("Event: {EventType} received for projectId:{projectId}, buildId:{buildId}", baseMessage.EventType, projectId, buildId);

        using Operation operation = _operations.BeginLoggingScope(
            "Build: {buildId}, Project: {projectId}, Org: {orgId}",
            buildId,
            projectId,
            orgId
        );

        _logger.LogInformation("HandleMessage for buildId: {buildId} and project: {projectId} and org: {orgId}", buildId, projectId, orgId);

        Build build = await _build.GetBuildAsync(orgId, projectId, buildId, cancellationToken);

        var buildReference = new NamedBuildReference(
            build.DefinitionName,
            build.Links.Web,
            orgId,
            projectId,
            buildId,
            build.Url,
            build.DefinitionId,
            build.DefinitionName,
            build.Repository.Name,
            build.CommitHash,
            build.TargetBranch?.BranchName
        );

        BuildAnalysisEvent lastAnalysisEvent = _tableService.GetLastBuildAnalysisRecord(build.Id, build.DefinitionName);

        if (lastAnalysisEvent != null && message.CreatedTime < lastAnalysisEvent.AnalysisTimestamp
            || await _processingStatusService.IsBuildBeingProcessed(_clock.UtcNow.AddDays(-1), buildReference.RepositoryId, buildId, cancellationToken))
        {
            _logger.LogInformation("Skipping processing of build {buildId} as it was recently processed or is under processing and there are no new changes", build.Id);
            return;
        }

        if (buildReference.Project.Equals(_internalProject.Id) || buildReference.Project.Equals(_internalProject.Path))
        {
            if (await _azDoToGitHubRepositoryService.IsInternalRepositorySupported(build.Repository, build.CommitHash))
            {
                string internalStorageContext = $"{_internalProject.Path}/{buildReference.RepositoryId}/{buildReference.SourceSha}";
                _contextualStorage.SetContext(internalStorageContext);
                await _buildAnalysisService.GetBuildResultAnalysisAsync(buildReference, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Repository {repositoryName} is not supported", buildReference.RepositoryId);
                await _tableService.SaveBuildAnalysisRepositoryNotSupported(buildReference.Name, buildReference.BuildId,
                    buildReference.RepositoryId, build.ProjectName, _clock.UtcNow);
            }
            return;
        }

        if (!await _gitHubChecksService.IsRepositorySupported(buildReference.RepositoryId))
        {
            _logger.LogInformation("Repository {repositoryName} is not supported", buildReference.RepositoryId);
            await _tableService.SaveBuildAnalysisRepositoryNotSupported(buildReference.Name, buildReference.BuildId,
                buildReference.RepositoryId, build.ProjectName, _clock.UtcNow);

            return;
        }

        if (!await _pipelineRequestedService.IsBuildPipelineRequested(buildReference.RepositoryId, buildReference.TargetBranch, buildReference.DefinitionId, buildReference.BuildId))
        {
            _logger.LogInformation("Pipeline {pipeline} of repository {repositoryName} has been filtered by repository configuration", build.DefinitionName, buildReference.RepositoryId);
            await _processingStatusService.SaveBuildAnalysisProcessingStatus(buildReference.RepositoryId, buildId, BuildProcessingStatus.Completed);
            return;
        }

        if (build.PullRequest != null)
        {
            try
            {
                await GenerateQueueInsights(build, buildReference, cancellationToken);
            }
            catch (KustoClientException e)
            {
                // Sometimes, Kusto will give us an IOException saying that "... The response ended prematurely"
                // Or a SocketException is thrown with "Unable to read data from the transport connection ... closed by the remote host."
                // In these cases, we should rethrow the exception to retry.
                _logger.LogWarning(e, "A Kusto related exception occurred when generating Queue Insights. Retrying the message.");
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to create Queue Insights for repo: {repo} pr: {pr}", build.Repository,
                    build.PullRequest);
            }
        }


        string snapshotId = $"{_clock.UtcNow:yyyy-MM-ddTHH-mm-ss}";
        _logger.LogInformation("Generating report for snapshot {snapshotId}", snapshotId);

        MergedBuildResultAnalysis analysis;
        try
        {
            await _processingStatusService.SaveBuildAnalysisProcessingStatus(buildReference.RepositoryId, buildId,
                BuildProcessingStatus.InProcess);

            string storageContext = $"{buildReference.RepositoryId}/{buildReference.SourceSha}";
            _contextualStorage.SetContext(storageContext);

            await using IDistributedLock blobLock = await _distributedLockService.AcquireAsync(storageContext, TimeSpan.FromMinutes(5), cancellationToken);
            _logger.LogInformation("Lock acquired for '{storageContext}'", storageContext);


            analysis = await _buildAnalysis.GetMergedAnalysisAsync(
                buildReference,
                build.IsComplete ? MergeBuildAnalysisAction.Include : MergeBuildAnalysisAction.Exclude,
                cancellationToken: cancellationToken
            );

            _logger.LogInformation("MergedBuildResultAnalysis created for build Id: '{buildId}'", buildReference.BuildId);
        }
        finally
        {
            await _processingStatusService.SaveBuildAnalysisProcessingStatus(buildReference.RepositoryId, buildId, BuildProcessingStatus.Completed);
        }

        BuildAnalysis.Models.Repository repository = new(buildReference.RepositoryId,
            await _gitHubChecksService.RepositoryHasIssues(buildReference.RepositoryId));

        string markdown = _markdownGenerator.GenerateMarkdown(
            new MarkdownParameters(analysis, snapshotId, build.PullRequestUrl, repository, _knownIssueUrlOptions));

        long checkRunId;
        try
        {
            checkRunId = await _gitHubChecksService.PostChecksResultAsync(
                CheckRunName,
                CheckRunOutputName,
                markdown,
                buildReference.RepositoryId,
                buildReference.SourceSha,
                analysis.OverallStatus,
                cancellationToken
            );
        }
        catch (ApiValidationException ex) when (Regex.IsMatch(ex.Message, "(.*)Only(.*)characters are allowed(.*)"))
        {
            string errorMatch = Regex.Match(ex.Message, "Only (\\d*) characters are allowed").Groups[1].Value;
            int checkCharactersLimit = int.Parse(errorMatch);

            markdown = _markdownGenerator.GenerateMarkdown(new MarkdownParameters(analysis, snapshotId, build.PullRequestUrl, repository,
                    _knownIssueUrlOptions, new MarkdownSummarizeInstructions(true, ErrorMessageLimitLength, TestKnownIssueDisplayLimit)));

            if (markdown.Length > checkCharactersLimit)
            {
                markdown = _markdownGenerator.GenerateMarkdown(new MarkdownParameters(analysis, snapshotId, build.PullRequestUrl, repository,
                        _knownIssueUrlOptions, new MarkdownSummarizeInstructions(generateSummaryVersion: true)));
            }

            checkRunId = await _gitHubChecksService.PostChecksResultAsync(
                CheckRunName,
                CheckRunOutputName,
                markdown,
                buildReference.RepositoryId,
                buildReference.SourceSha,
                analysis.OverallStatus,
                cancellationToken
            );
        }

        DateTimeOffset analysisTimestamp = DateTimeOffset.UtcNow;
        await _tableService.SaveBuildAnalysisRecords(analysis.CompletedPipelines, buildReference.RepositoryId, build.ProjectName, analysisTimestamp);

        _logger.LogInformation(
            "Created check run {checkRunId} triggered by build {buildId} for commit {commitHash}",
            checkRunId,
            buildId,
            buildReference.SourceSha
        );

        _telemetry.TrackEvent(
            "DevWfCheckCreated",
            new Dictionary<string, string>
            {
            {"checkRunId", checkRunId.ToString()},
            {"buildId", buildId.ToString()},
            {"commitHash", buildReference.SourceSha},
            {"snapshotId", snapshotId},
            {"pullRequest", build.PullRequest}
            },
            new Dictionary<string, double>
            {
            {"markdownLength", markdown.Length},
            }
        );

        try
        {
            // Save these values so that we can diagnose reported feedback using the snapshot ID
            await using var mdStream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));
            await _contextualStorage.PutAsync($"markdown-{snapshotId}.md", mdStream, CancellationToken.None);

            await StreamHelpers.StreamDataAsync(
                w => JsonSerializer.SerializeAsync(
                    w,
                    analysis,
                    new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull },
                    CancellationToken.None
                ),
                r => _contextualStorage.PutAsync($"analysis-blob-{snapshotId}.json", r, cancellationToken)
            );
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to save markdown/analysis");
        }
    }

    private async Task UpdateBuildAnalysisCheckRun(CheckRunConclusionUpdateMessage checkRunConclusionUpdateMessage)
    {
        GitHub.Models.CheckRun buildAnalysisCheckRun = await _gitHubChecksService.GetCheckRunAsyncForApp(
            checkRunConclusionUpdateMessage.Repository, checkRunConclusionUpdateMessage.HeadSha, _gitHubTokenProviderOptions.GitHubAppId, CheckRunName);

        if (buildAnalysisCheckRun == null)
        {
            _logger.LogInformation("Unable to find Build Analysis check run of commit {commit} on repository {repository}",
                checkRunConclusionUpdateMessage.HeadSha, checkRunConclusionUpdateMessage.Repository);
            return;
        }

        if (string.IsNullOrWhiteSpace(checkRunConclusionUpdateMessage.Justification))
        {
            string justificationMissingMessage = $"Unable to override the build analysis check run because no reason was provided. Please provide a reason. Post a request in the following format: {BuildAnalysisEscapeMechanismHelper.ChangeToGreenCommand} <*reason*>";
            await _githubIssuesService.AddCommentToIssueAsync(checkRunConclusionUpdateMessage.Repository, checkRunConclusionUpdateMessage.IssueNumber, justificationMissingMessage);
            return;
        }

        var buildAnalysisUpdateOverrideResultView = new BuildAnalysisUpdateOverridenResult(checkRunConclusionUpdateMessage.Justification, buildAnalysisCheckRun.Conclusion.ToString(),
            checkRunConclusionUpdateMessage.CheckResultString, buildAnalysisCheckRun.Body);

        _logger.LogInformation("Starting update of build analysis check run from {prevStatus} to {newStatus}", buildAnalysisCheckRun.Status.ToString(), checkRunConclusionUpdateMessage.CheckResultString);
        string markdownAnalysisOverridenResult = _markdownGenerator.GenerateMarkdown(buildAnalysisUpdateOverrideResultView);
        await _gitHubChecksService.UpdateCheckRunConclusion(buildAnalysisCheckRun, checkRunConclusionUpdateMessage.Repository, markdownAnalysisOverridenResult, checkRunConclusionUpdateMessage.GetCheckConclusion());
        _logger.LogInformation("Build analysis check run updated");

        _logger.LogInformation("Saving builds that were part of the check run analyzed");
        List<GitHub.Models.CheckRun> buildCheckRuns = (await _gitHubChecksService.GetBuildCheckRunsAsync(checkRunConclusionUpdateMessage.Repository, checkRunConclusionUpdateMessage.HeadSha)).ToList();
        List<(string repository, int buildId)> buildCheckRunsToUpdate = buildCheckRuns.Select(t => (checkRunConclusionUpdateMessage.Repository, t.AzureDevOpsBuildId)).ToList();
        await _processingStatusService.SaveBuildAnalysisProcessingStatus(buildCheckRunsToUpdate, BuildProcessingStatus.ConclusionOverridenByUser);
        _logger.LogInformation("Builds saved as overridden: {buildsOverridden}", buildCheckRunsToUpdate.Select(t => t.buildId));

    }

    private async Task GenerateQueueInsights(Build build, BuildReferenceIdentifier buildReference,
        CancellationToken cancellationToken)
    {
        RelatedBuilds relatedBuilds =
            await _relatedBuildService.GetRelatedBuilds(buildReference, cancellationToken);

        ImmutableHashSet<int> definitions = relatedBuilds.RelatedBuildsList.Select(x => x.DefinitionId)
            .Append(buildReference.DefinitionId).ToImmutableHashSet();

        _logger.LogInformation("Found Pipelines: {pipelines} for repo: {repo} pr: {pr}",
            string.Join(", ", definitions), build.Repository, build.PullRequest);

        IEnumerable<KnownIssue> criticalIssues =
            await _githubIssuesService.GetCriticalInfrastructureIssuesAsync();

        long checkId = await _queueInsightsService.CreateQueueInsightsAsync(
            build.Repository.Name,
            build.CommitHash,
            build.PullRequest,
            definitions,
            build.TargetBranch.BranchName,
            criticalIssues.Any(),
            cancellationToken);

        _logger.LogInformation("Created Queue Insights Check: {checkId} repo: {repo} pr: {pr}", checkId,
            build.Repository, build.PullRequest);

    }

    private async Task ValidateKnownIssueMessage(KnownIssueValidationMessage knownIssueValidationMessage, CancellationToken cancellationToken)
    {
        if (await _gitHubChecksService.IsRepositorySupported(knownIssueValidationMessage.RepositoryWithOwner))
        {
            await _knownIssueValidationService.ValidateKnownIssue(knownIssueValidationMessage, cancellationToken);
        }
    }
}

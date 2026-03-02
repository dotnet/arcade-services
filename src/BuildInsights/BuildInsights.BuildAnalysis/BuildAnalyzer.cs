// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BuildInsights.AzureStorage.Cache;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.Data.Models;
using BuildInsights.GitHub;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using BuildInsights.QueueInsights;
using Kusto.Data.Exceptions;
using Microsoft.ApplicationInsights;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

#nullable disable
namespace BuildInsights.BuildAnalysis;

public interface IBuildAnalyzer
{
    Task AnalyzeBuild(
        string orgId,
        string projectId,
        int buildId,
        DateTimeOffset requestCreationTime,
        CancellationToken cancellationToken);
}

public class BuildAnalyzer : IBuildAnalyzer
{
    private const string CheckRunName = "Build Analysis";
    private const string CheckRunOutputName = ".NET Result Analysis";
    private const int ErrorMessageLimitLength = 1000;
    private const int TestKnownIssueDisplayLimit = 50;

    private readonly IMergedBuildAnalysisService _buildAnalysis;
    private readonly IBuildAnalysisService _buildAnalysisService;
    private readonly IMarkdownGenerator _markdownGenerator;
    private readonly IGitHubChecksService _gitHubChecksService;
    private readonly IBuildDataService _build;
    private readonly ILogger<BuildAnalyzer> _logger;
    private readonly TelemetryClient _telemetry;
    private readonly IContextualStorage _contextualStorage;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ISystemClock _clock;
    private readonly KnownIssueUrlOptions _knownIssueUrlOptions;
    private readonly InternalProjectSettings _internalProject;
    private readonly OperationManager _operations;
    private readonly IBuildAnalysisHistoryService _analysisHistory;
    private readonly IBuildProcessingStatusService _processingStatusService;
    private readonly IRelatedBuildService _relatedBuildService;
    private readonly IQueueInsightsService _queueInsightsService;
    private readonly IGitHubIssuesService _githubIssuesService;
    private readonly IAzDoToGitHubRepositoryService _azDoToGitHubRepositoryService;
    private readonly IPipelineRequestedService _pipelineRequestedService;

    public BuildAnalyzer(
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
        IOptions<KnownIssueUrlOptions> knownIssueUrlOptions,
        IOptions<InternalProjectSettings> internalProject,
        IBuildAnalysisHistoryService analysisHistory,
        IBuildProcessingStatusService processingStatusService,
        ILogger<BuildAnalyzer> logger,
        IRelatedBuildService relatedBuildService,
        IQueueInsightsService queueInsightsService,
        IGitHubIssuesService gitHubIssuesService,
        IKnownIssueValidationService knownIssueValidationService,
        IAzDoToGitHubRepositoryService azDoToGitHubRepositoryService,
        IPipelineRequestedService pipelineRequestedService,
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
        _analysisHistory = analysisHistory;
        _processingStatusService = processingStatusService;
        _relatedBuildService = relatedBuildService;
        _queueInsightsService = queueInsightsService;
        _githubIssuesService = gitHubIssuesService;
        _azDoToGitHubRepositoryService = azDoToGitHubRepositoryService;
        _pipelineRequestedService = pipelineRequestedService;
    }

    public async Task AnalyzeBuild(
        string orgId,
        string projectId,
        int buildId,
        DateTimeOffset requestCreationTime,
        CancellationToken cancellationToken)
    {
        using Operation operation = _operations.BeginLoggingScope(
            "Build: {buildId}, Project: {projectId}, Org: {orgId}",
            buildId,
            projectId,
            orgId);

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

        BuildAnalysisEvent lastAnalysisEvent = await _analysisHistory.GetLastBuildAnalysisRecord(build.Id, build.DefinitionName);

        if (lastAnalysisEvent != null
            && requestCreationTime < lastAnalysisEvent.AnalysisTimestamp
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
                await _analysisHistory.SaveBuildAnalysisRepositoryNotSupported(buildReference.Name, buildReference.BuildId,
                    buildReference.RepositoryId, build.ProjectName, _clock.UtcNow);
            }
            return;
        }

        if (!await _gitHubChecksService.IsRepositorySupported(buildReference.RepositoryId))
        {
            _logger.LogInformation("Repository {repositoryName} is not supported", buildReference.RepositoryId);
            await _analysisHistory.SaveBuildAnalysisRepositoryNotSupported(buildReference.Name, buildReference.BuildId,
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
            await _processingStatusService.SaveBuildAnalysisProcessingStatus(
                buildReference.RepositoryId,
                buildId,
                BuildProcessingStatus.InProcess);

            string storageContext = $"{buildReference.RepositoryId}/{buildReference.SourceSha}";
            _contextualStorage.SetContext(storageContext);

            await using IDistributedLock blobLock = await _distributedLockService.AcquireAsync(storageContext, TimeSpan.FromMinutes(5), cancellationToken);
            _logger.LogInformation("Lock acquired for '{storageContext}'", storageContext);


            analysis = await _buildAnalysis.GetMergedAnalysisAsync(
                buildReference,
                build.IsComplete ? MergeBuildAnalysisAction.Include : MergeBuildAnalysisAction.Exclude,
                cancellationToken: cancellationToken);

            _logger.LogInformation("MergedBuildResultAnalysis created for build Id: '{buildId}'", buildReference.BuildId);
        }
        finally
        {
            await _processingStatusService.SaveBuildAnalysisProcessingStatus(buildReference.RepositoryId, buildId, BuildProcessingStatus.Completed);
        }

        Models.Repository repository = new(buildReference.RepositoryId,
            await _gitHubChecksService.RepositoryHasIssues(buildReference.RepositoryId));

        string markdown = _markdownGenerator.GenerateMarkdown(
            new MarkdownParameters(
                analysis,
                snapshotId,
                build.PullRequestUrl,
                repository,
                _knownIssueUrlOptions));

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
                cancellationToken);
        }
        catch (ApiValidationException ex) when (Regex.IsMatch(ex.Message, "(.*)Only(.*)characters are allowed(.*)"))
        {
            string errorMatch = Regex.Match(ex.Message, "Only (\\d*) characters are allowed").Groups[1].Value;
            int checkCharactersLimit = int.Parse(errorMatch);

            markdown = _markdownGenerator.GenerateMarkdown(
                new MarkdownParameters(
                    analysis,
                    snapshotId,
                    build.PullRequestUrl,
                    repository,
                    _knownIssueUrlOptions,
                    new MarkdownSummarizeInstructions(true, ErrorMessageLimitLength, TestKnownIssueDisplayLimit)));

            if (markdown.Length > checkCharactersLimit)
            {
                markdown = _markdownGenerator.GenerateMarkdown(
                    new MarkdownParameters(
                        analysis,
                        snapshotId,
                        build.PullRequestUrl,
                        repository,
                        _knownIssueUrlOptions,
                        new MarkdownSummarizeInstructions(generateSummaryVersion: true)));
            }

            checkRunId = await _gitHubChecksService.PostChecksResultAsync(
                CheckRunName,
                CheckRunOutputName,
                markdown,
                buildReference.RepositoryId,
                buildReference.SourceSha,
                analysis.OverallStatus,
                cancellationToken);
        }

        DateTimeOffset analysisTimestamp = DateTimeOffset.UtcNow;
        await _analysisHistory.SaveBuildAnalysisRecords(analysis.CompletedPipelines, buildReference.RepositoryId, build.ProjectName, analysisTimestamp);

        _logger.LogInformation(
            "Created check run {checkRunId} triggered by build {buildId} for commit {commitHash}",
            checkRunId,
            buildId,
            buildReference.SourceSha);

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
            });

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

    private async Task GenerateQueueInsights(
        Build build,
        BuildReferenceIdentifier buildReference,
        CancellationToken cancellationToken)
    {
        RelatedBuilds relatedBuilds =
            await _relatedBuildService.GetRelatedBuilds(buildReference, cancellationToken);

        ImmutableHashSet<int> definitions = relatedBuilds.RelatedBuildsList
            .Select(x => x.DefinitionId)
            .Append(buildReference.DefinitionId)
            .ToImmutableHashSet();

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
}

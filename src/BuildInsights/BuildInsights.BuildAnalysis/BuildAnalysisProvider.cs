// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.GitHub.Models;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using BuildInsights.Utilities.AzureDevOps.Models;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildInsights.BuildAnalysis;

public interface IBuildAnalysisService
{
    public Task<BuildResultAnalysis> GetBuildResultAnalysisAsync(BuildReferenceIdentifier buildReference, CancellationToken cancellationToken, bool isValidationAnalysis = false);
}

public class BuildAnalysisProvider : IBuildAnalysisService
{
    private readonly IBuildDataService _buildDataService;
    private readonly IOptions<AzureDevOpsSettingsCollection> _azdoSettings;
    private readonly ILogger<BuildAnalysisProvider> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly IBuildCacheService _cache;
    private readonly IBuildRetryService _buildRetryService;
    private readonly IGitHubIssuesService _gitHubIssuesService;
    private readonly IKnownIssuesService _knownIssuesService;
    private readonly IHelixDataService _helixDataService;
    private readonly ITestResultService _testResultService;
    private readonly IAzDoToGitHubRepositoryService _azDoToGitHubRepositoryProvider;
    private readonly IKnownIssuesMatchService _knownIssuesMatchService;
    private readonly KnownIssuesAnalysisLimits _analysisLimits;
    private readonly InternalProject _internalProject;

    public BuildAnalysisProvider(
        IBuildDataService buildDataService,
        IBuildRetryService buildRetryService,
        IOptions<AzureDevOpsSettingsCollection> azdoSettings,
        IOptions<KnownIssuesAnalysisLimits> analysisLimits,
        IOptions<InternalProject> internalProject,
        ILogger<BuildAnalysisProvider> logger,
        TelemetryClient telemetryClient,
        IGitHubIssuesService gitHubIssues,
        IKnownIssuesService knownIssuesService,
        IHelixDataService helixDataService,
        ITestResultService testResultService,
        IAzDoToGitHubRepositoryService azDoToGitHubRepositoryProvider,
        IKnownIssuesMatchService knownIssuesMatchService,
        IBuildCacheService cache = null)
    {
        _buildDataService = buildDataService;
        _buildRetryService = buildRetryService;
        _azdoSettings = azdoSettings;
        _internalProject = internalProject.Value;
        _logger = logger;
        _telemetryClient = telemetryClient;
        _cache = cache;
        _gitHubIssuesService = gitHubIssues;
        _knownIssuesService = knownIssuesService;
        _helixDataService = helixDataService;
        _testResultService = testResultService;
        _knownIssuesMatchService = knownIssuesMatchService;
        _analysisLimits = analysisLimits.Value;
        _azDoToGitHubRepositoryProvider = azDoToGitHubRepositoryProvider;
    }

    public async Task<BuildResultAnalysis> GetBuildResultAnalysisAsync(
        BuildReferenceIdentifier buildReference,
        CancellationToken cancellationToken,
        bool isValidationAnalysis = false)
    {
        // Everything starts with the build
        Build build = await _buildDataService.GetBuildAsync(
            buildReference.Org,
            buildReference.Project,
            buildReference.BuildId,
            cancellationToken
        );

        // Populate the rendering data object
        var buildAnalysis = new BuildResultAnalysis
        {
            PipelineName = build.DefinitionName,
            BuildId = build.Id,
            BuildNumber = build.BuildNumber,
            TargetBranch = build.TargetBranch,
            LinkToBuild = build.Links.Web,
            LinkAllTestResults = BuildTestResultsTabUri(build),
            Attempt = await BuildAttempt(buildReference.Org, buildReference.Project, build.Id, cancellationToken),
            BuildStatus = GetBuildStatus(build),
            BuildStepsResult = [],
            TestResults = []
        };

        buildAnalysis.IsRerun = buildAnalysis.Attempt > 1;

        if (build.ValidationResults.Count > 0)
        {
            StepResult validationStepResult = new StepResult
            {
                StepName = "Pipeline Definition Validation"
            };

            foreach (BuildValidationResult validationResult in build.ValidationResults.Where(x => x.Result == BuildValidationStatus.Error))
            {
                validationStepResult.Errors.Add(new Error
                {
                    ErrorMessage = validationResult.Message
                });
            }

            buildAnalysis.BuildStepsResult.Add(validationStepResult);
        }

        IReadOnlyList<TimelineRecord> buildTimelineRecords = await _buildDataService.GetLatestBuildTimelineRecordsAsync(buildReference.Org, buildReference.Project, buildReference.BuildId, cancellationToken);
        if (isValidationAnalysis)
        {
            buildTimelineRecords = await _buildDataService.GetTimelineRecordsFromAllAttempts(buildTimelineRecords, buildReference.Org, buildReference.Project, buildReference.BuildId, cancellationToken);
        }

        var succeededWithIssuesPipelineReference = new List<PipelineReference>();
        List<KnownIssue> knownIssues = (await GetKnownIssues(build)).ToList();
        if (buildTimelineRecords?.Count > 0)
        {
            Dictionary<Guid, TimelineRecord> recordDictionary = buildTimelineRecords.ToDictionary(r => r.Id);
            ImmutableList<TimelineRecord> recordsOrdered = RecordNode.GetTimelineRecordsOrdererByTreeStructure(buildTimelineRecords);
            IEnumerable<TimelineRecord> failedTasks = recordsOrdered.Where(x => x.Result == TaskResult.Failed && (x.RecordType == RecordType.Task || x.RecordType == RecordType.Job));
            IEnumerable<TimelineRecord> canceledAndAbandonedJobsAndTasks = recordsOrdered.Where(x => (x.Result == TaskResult.Canceled || x.Result == TaskResult.Abandoned) &&
                (x.RecordType == RecordType.Job || x.RecordType == RecordType.Task));

            buildAnalysis.BuildStepsResult = await GetStepsResultsAsync(canceledAndAbandonedJobsAndTasks.Concat(failedTasks).Take(_analysisLimits.RecordCountLimit), recordDictionary, buildReference, knownIssues);

            IEnumerable<Guid> taskSucceededWithIssues = buildTimelineRecords
                .Where(t => t.Result == TaskResult.SucceededWithIssues && t.RecordType == RecordType.Job)
                .Select(t => t.Id);
            succeededWithIssuesPipelineReference = taskSucceededWithIssues
                .Select(taskId => BuildPipelineReference(taskId, recordDictionary)).Where(p => p != null).ToList();
        }

        // Get all failing TestCaseResults and their associated TestRuns

        List<TestRunDetails> testResultsCausingBuildToFail;
        if (isValidationAnalysis)
        {
            IReadOnlyList<TestRunDetails> allFailuresTestCaseResults = await _buildDataService.GetAllFailingTestsForBuildAsync(build, cancellationToken);
            testResultsCausingBuildToFail = allFailuresTestCaseResults.ToList();
        }
        else
        {
            List<TestRunDetails> failingTestCaseResults = await _buildDataService.GetFailingTestsForBuildAsync(build, cancellationToken);
            //Filter results that where executed on phases that finished successfully
            testResultsCausingBuildToFail = failingTestCaseResults.Where(t => succeededWithIssuesPipelineReference.All(p => !p.Equals(t.PipelineReference))).ToList();
        }

        TestKnownIssuesAnalysis testKnownIssuesAnalysis = await _testResultService.GetTestFailingWithKnownIssuesAnalysis(testResultsCausingBuildToFail, knownIssues.ToList(), buildReference.Org);
        buildAnalysis.TestKnownIssuesAnalysis = testKnownIssuesAnalysis;
        buildAnalysis.TotalTestFailures = testResultsCausingBuildToFail.Sum(t => t.Results.Count);
        buildAnalysis.TestResults.AddRange(await GetTestResults(testResultsCausingBuildToFail, buildReference, buildAnalysis.TargetBranch, testKnownIssuesAnalysis, cancellationToken)); // short name

        //If the build succeeds gathering information about previous failures
        if (buildAnalysis.BuildStatus == BuildStatus.Succeeded && !isValidationAnalysis)
        {
            //If it is a retry and the build succeed we are going to return the attempt previous to the succeed
            if (buildAnalysis.IsRerun)
            {
                _logger.LogInformation($"BuildId {build.Id} in org {buildReference.Org} in project {buildReference.Project} has completed successfully after a retry. Fetching test failures previous attempts.");
                IReadOnlyList<TestRunDetails> allFailuresTestCaseResults = await _buildDataService.GetAllFailingTestsForBuildAsync(build, cancellationToken);
                buildAnalysis.LatestAttempt = new Attempt
                {
                    TestResults = await GetTestResults(allFailuresTestCaseResults, buildReference, buildAnalysis.TargetBranch, new TestKnownIssuesAnalysis(), cancellationToken)
                };

                _logger.LogInformation($"BuildId {build.Id} in org {buildReference.Org} in project {buildReference.Project}. Fetching timeline records from previous attempt");
                IReadOnlyList<TimelineRecord> timelineRecordsPreviousAttempt =
                    await GetRecordsFromLatestPreviousAttempt(buildTimelineRecords, buildReference, cancellationToken);
                _logger.LogInformation($"BuildId {build.Id} in org {buildReference.Org} in project {buildReference.Project} / has a count of {timelineRecordsPreviousAttempt.Count} records on previous attempt");

                if (timelineRecordsPreviousAttempt.Count > 0)
                {
                    Dictionary<Guid, TimelineRecord> recordDictionary = timelineRecordsPreviousAttempt.ToDictionary(r => r.Id);
                    ImmutableList<TimelineRecord> timelineRecordsPreviousAttemptOrdered = RecordNode.GetTimelineRecordsOrdererByTreeStructure(timelineRecordsPreviousAttempt);
                    List<TimelineRecord> failedTaskInPreviousAttempt = timelineRecordsPreviousAttemptOrdered.Where(x => x.Result == TaskResult.Failed && x.RecordType == RecordType.Task).ToList();

                    _logger.LogInformation($"BuildId {build.Id} in org {buildReference.Org} in project {buildReference.Project} / has {failedTaskInPreviousAttempt.Count} failed records on previous attempt");

                    buildAnalysis.LatestAttempt.AttemptId = timelineRecordsPreviousAttempt.First().Attempt;
                    buildAnalysis.LatestAttempt.BuildStepsResult = await GetStepsResultsAsync(failedTaskInPreviousAttempt.Take(_analysisLimits.RecordCountLimit), recordDictionary, buildReference, knownIssues);
                }
                else
                {
                    _logger.LogInformation($"BuildId {build.Id} in org {buildReference.Org} in project {buildReference.Project} was not able to fetch record from a previous attempt ");
                }
            }

            buildAnalysis.TestResults.AddRange(await GetTestResults(ImmutableList<TestRunDetails>.Empty, buildReference, buildAnalysis.TargetBranch, new TestKnownIssuesAnalysis(), cancellationToken));
        }

        // Update or remove build messages
        CleanBuildMessages(buildAnalysis.BuildStepsResult, testResultsCausingBuildToFail, buildTimelineRecords?.ToDictionary(r => r.Id));

        //Verified the status of the build to be sure that hasn't be retry manually
        if (build.Result == BuildResult.Failed)
        {
            buildAnalysis.BuildAutomaticRetry = await TryRetryBuild(buildReference, cancellationToken, buildAnalysis);
            if (buildAnalysis.BuildAutomaticRetry.HasRerunAutomatically)
            {
                buildAnalysis.BuildStatus = BuildStatus.InProgress;
            }
        }

        var metrics = new Dictionary<string, double>
        {
            {"failedSteps", buildAnalysis.BuildStepsResult.Count},
            {"errorMessages", buildAnalysis.BuildStepsResult.Sum(s => s.Errors.Count)},
            {"knownIssues", buildAnalysis.BuildStepsResult.Sum(t => t.KnownIssues.Count)},
            {"failedTests", buildAnalysis.TestResults.Count},
            {"failedConfigs", buildAnalysis.TestResults.Sum(t => t.FailingConfigurations.Count)},
            {"failedTestsUnique", buildAnalysis.TestResults.Count(t => t.KnownIssues.Count == 0 && !t.IsKnownIssueFailure)},
            {"buildFailuresUnique", buildAnalysis.BuildStepsResult.Count(t => t.KnownIssues.Count == 0)},
            {"totalTestFailures", buildAnalysis.TotalTestFailures},
            {"testKnownIssue", buildAnalysis.TestKnownIssuesAnalysis.TestResultWithKnownIssues.Count }
        };

        _telemetryClient.TrackEvent(
            "BuildAnalysisComplete",
            new Dictionary<string, string>
            {
                {"buildId", build.Id.ToString()},
                {"repository", build.Repository.Name},
                {"project", build.ProjectName},
                {"reason", build.Reason},
                {"commitHash", build.CommitHash},
                {"attempt", buildAnalysis.Attempt.ToString()},
                {"definitionName", build.DefinitionName},
                {"uniqueBuild", buildAnalysis.HasBuildFailures ? "1" : "0"},
                {"uniqueTest", buildAnalysis.HasTestFailures ? "1" : "0"},
                {"retryBuild", buildAnalysis.IsRerun && buildAnalysis.LatestAttempt != null && buildAnalysis.LatestAttempt.HasBuildFailures ? "1" : "0"},
                {"retryTest", buildAnalysis.IsRerun && buildAnalysis.LatestAttempt != null && buildAnalysis.LatestAttempt.HasTestFailures ? "1" : "0"},
                {"isRerun", buildAnalysis.IsRerun ? "1" : "0"},
                {"isValidationAnalysis", isValidationAnalysis ? "1" : "0"},
                {"retryBuildAutomatically", buildAnalysis.BuildAutomaticRetry != null && buildAnalysis.BuildAutomaticRetry.HasRerunAutomatically ? "1" : "0"},
                {"buildRetriedByKnownIssue", buildAnalysis.BuildAutomaticRetry?.GitHubIssue?.LinkGitHubIssue ?? string.Empty}
            },
            metrics
        );

        CaptureStepsTelemetry(buildAnalysis, build);

        await _knownIssuesService.SaveKnownIssuesHistory(knownIssues, build.Id);

        await _knownIssuesService.SaveKnownIssuesMatches(build.Id, KnownIssuesMatchHelper.GetKnownIssueMatchesInBuild(build, buildAnalysis));
        await _knownIssuesService.SaveTestsKnownIssuesMatches(build.Id, KnownIssuesMatchHelper.GetKnownIssueMatchesInTests(build, buildAnalysis));

        if (_cache != null)
        {
            await _cache.PutBuildAsync(buildReference, buildAnalysis, cancellationToken);
        }

        return buildAnalysis;
    }

    private async Task<IEnumerable<KnownIssue>> GetKnownIssues(Build build)
    {
        IEnumerable<KnownIssue> infrastructureKnownIssues = await _gitHubIssuesService.GetInfrastructureKnownIssues();
        List<KnownIssue> knownIssues = infrastructureKnownIssues.ToList();

        if (build.ProjectId?.Equals(_internalProject.Id) ?? false)
        {
            AzDoToGitHubRepositoryResult result = await _azDoToGitHubRepositoryProvider.TryGetGitHubRepositorySupportingKnownIssues(build.Repository, build.CommitHash);

            if (result.IsValidRepositoryAvailable)
            {
                knownIssues.AddRange(await _gitHubIssuesService.GetRepositoryKnownIssues(result.GitHubRepository));
            }
        }
        else
        {
            knownIssues.AddRange(await _gitHubIssuesService.GetRepositoryKnownIssues(build.Repository.Name));
        }

        knownIssues = knownIssues.Where(issue => issue.BuildError is { Count: > 0 })
            .DistinctBy(k => k.GitHubIssue, new GitHubIssueComparer()).ToList();

        _logger.LogInformation($"Analyzing {knownIssues.Count} known issues in build: {build.Id}");
        return knownIssues;
    }

    private async Task<BuildAutomaticRetry> TryRetryBuild(BuildReferenceIdentifier buildReference, CancellationToken cancellationToken, BuildResultAnalysis buildAnalysis)
    {
        if (await _buildRetryService.RetryIfSuitable(buildReference.Org, buildReference.Project, buildReference.BuildId, cancellationToken))
        {
            _logger.LogInformation($"BuildId: {buildReference.BuildId} from repo: {buildReference.RepositoryId} in org {buildReference.Org} has been successfully retried");
            return new BuildAutomaticRetry(true);
        }

        KnownIssue knownIssueWithBuildRetry = buildAnalysis.BuildStepsResult.SelectMany(b => b.KnownIssues).FirstOrDefault(k => k.Options.RetryBuild);
        if (knownIssueWithBuildRetry != null && await _buildRetryService.RetryIfKnownIssueSuitable(buildReference.Org, buildReference.Project, buildReference.BuildId, cancellationToken))
        {
            _logger.LogInformation(
                $"BuildId: {buildReference.BuildId} from repo: {buildReference.RepositoryId} has been successfully retried because of known issue {knownIssueWithBuildRetry.GitHubIssue.Repository}/{knownIssueWithBuildRetry.GitHubIssue.Id}");
            return new BuildAutomaticRetry(true, knownIssueWithBuildRetry.GitHubIssue);
        }

        return new BuildAutomaticRetry(false);
    }

    private string CreateLinkToStep(BuildReferenceIdentifier buildReference, TimelineRecord record)
    {
        return $"{_azdoSettings.Value.Settings.First(x => x.OrgId == buildReference.Org).CollectionUri}/{buildReference.Project}/_build/results?buildId={buildReference.BuildId}&view=logs&j={record.ParentId:D}";
    }

    private string CreateConsoleLogLink(BuildReferenceIdentifier buildReference, TimelineRecord record, TimelineIssue issue)
    {
        string uri = $"{_azdoSettings.Value.Settings.First(x => x.OrgId == buildReference.Org).CollectionUri}/{buildReference.Project}/_build/results?buildId={buildReference.BuildId}&view=logs&j={record.ParentId:D}&t={record.Id}";

        if (issue.Data.TryGetValue("logFileLineNumber", out string lineNumber))
        {
            uri += $"&l={lineNumber}";
        }

        return uri;
    }

    private static PipelineReference BuildPipelineReference(Guid jobId, Dictionary<Guid, TimelineRecord> recordDictionary)
    {
        JobReference jobReference = null;
        PhaseReference phaseReference = null;
        StageReference stageReference = null;

        Guid idToSearchFor = jobId;
        while (true)
        {
            TimelineRecord foundRecord = recordDictionary[idToSearchFor];

            switch (foundRecord.RecordType)
            {
                case RecordType.Job:
                    jobReference = new JobReference(foundRecord.Identifier, foundRecord.Attempt);
                    break;
                case RecordType.Phase:
                    phaseReference = new PhaseReference(foundRecord.Identifier, foundRecord.Attempt);
                    break;
                case RecordType.Stage:
                    stageReference = new StageReference(foundRecord.Identifier, foundRecord.Attempt);
                    break;
            }

            if (!foundRecord.ParentId.HasValue) break;

            idToSearchFor = foundRecord.ParentId.Value;
        }

        if (jobReference == null || phaseReference == null || stageReference == null) return null;

        return new PipelineReference(stageReference, phaseReference, jobReference);
    }

    private static List<string> BuildStepHierarchy(Guid taskId, Dictionary<Guid, TimelineRecord> recordDictionary)
    {
        List<string> stepHierarchy = [];

        Guid idToSearchFor = taskId;
        while (true)
        {
            TimelineRecord foundRecord = recordDictionary[idToSearchFor];

            stepHierarchy.Insert(0, foundRecord.Name);

            if (!foundRecord.ParentId.HasValue)
            {
                break;
            }

            idToSearchFor = foundRecord.ParentId.Value;
        }

        return stepHierarchy;
    }

    private void CaptureStepsTelemetry(BuildResultAnalysis buildAnalysis, Build build)
    {
        foreach (StepResult stepResult in buildAnalysis.BuildStepsResult)
        {
            var stepMetrics = new Dictionary<string, double>
            {
                {"knownIssues", stepResult.KnownIssues.Count},
                {"uniquesIssues", stepResult.Errors.Count}
            };

            _telemetryClient.TrackEvent(
                "CheckRunConclusion",
                new Dictionary<string, string>
                {
                    {"buildId", build.Id.ToString()},
                    {"repository", build.Repository.Name},
                    {"commitHash", build.CommitHash},
                    {"attempt", buildAnalysis.Attempt.ToString()},
                    {"stepName", stepResult.StepName}
                }, stepMetrics);
        }
    }

    // Build the list of FailingConfigurations for the given AutomatedTestName
    private static List<FailingConfiguration> BuildFailingConfiguration(
        BuildReferenceIdentifier buildReferenceIdentifier,
        IEnumerable<TestRunDetails> runs,
        string automatedTestName)
    {
        IEnumerable<(string name, TestCaseResult testCaseResult)> runNamesAndTestCase = runs
            .Where(
                run => run.Results.Any(result => result.Name.Equals(automatedTestName))
            )
            .Select(run => (run.Name, run.Results.First(t => t.Name.Equals(automatedTestName))));

        return runNamesAndTestCase.Select(t => new FailingConfiguration
        {
            Configuration = new Configuration(GetTestConfigurationName(t.name), buildReferenceIdentifier.Org,
                buildReferenceIdentifier.Project, t.testCaseResult)
        }).ToList();
    }

    private static string GetTestConfigurationName(string configuration)
    {
        string dockerIdentifier = "@";
        if (configuration.Contains(dockerIdentifier))
        {
            int startIndex = configuration.IndexOf("(") + 1;
            int endIndex = configuration.IndexOf(')', startIndex);

            if (startIndex > 0 && endIndex > 0)
            {
                return configuration.Substring(startIndex, endIndex - startIndex);
            }
        }

        return configuration;
    }

    private static BuildStatus GetBuildStatus(Build build)
    {
        return build.Result switch
        {
            BuildResult.Succeeded => BuildStatus.Succeeded,
            BuildResult.PartiallySucceeded => BuildStatus.Succeeded,
            BuildResult.Failed => BuildStatus.Failed,
            _ => BuildStatus.Failed
        };
    }

    private async Task<List<StepResult>> GetStepsResultsAsync(IEnumerable<TimelineRecord> failedTasks, Dictionary<Guid, TimelineRecord> recordDictionary, BuildReferenceIdentifier buildReference, List<KnownIssue> knownIssues)
    {
        List<StepResult> result = [];

        foreach (TimelineRecord record in failedTasks)
        {
            var stepResult = new StepResult
            {
                StepHierarchy = BuildStepHierarchy(record.Id, recordDictionary),
                JobId = record.Id.ToString(),
                LinkLog = record.LogUrl,
                LinkToStep = CreateLinkToStep(buildReference, record),
                StepStartTime = record.StartTime,
                StepName = record.Name
            };

            var issues = new List<KnownIssue>();
            if (int.TryParse(record.LogUrl?.Split("/").Last(), out int logId) && record.RecordType != RecordType.Job)
            {
                Stream log = await _buildDataService.GetLogContent(buildReference.Org, buildReference.Project, buildReference.BuildId, logId);
                issues.AddRange(await _knownIssuesMatchService.GetKnownIssuesInStream(log, knownIssues));
            }

            IEnumerable<TimelineIssue> uniqueIssues = record.Issues.GroupBy(i => i.Message)
                .Select(g => g.First());

            foreach (TimelineIssue issue in uniqueIssues)
            {
                if (issue.Type != IssueType.Error) continue;

                var error = new Error
                {
                    ErrorMessage = issue.Message
                };

                issues.AddRange(_knownIssuesMatchService.GetKnownIssuesInString(issue.Message, knownIssues));

                if (record.LogUrl != null)
                {
                    error.LinkLog = CreateConsoleLogLink(buildReference, record, issue);
                }
                stepResult.Errors.Add(error);
            }

            _logger.LogInformation($"Count of known issues found on step errors: {issues.Distinct().Count()}");
            stepResult.KnownIssues = issues.Distinct().ToImmutableList();
            result.Add(stepResult);
        }

        return result;
    }

    private async Task<List<TestResult>> GetTestResults(
        IReadOnlyList<TestRunDetails> failingTestCaseResults,
        BuildReferenceIdentifier buildReference,
        Branch targetBranch,
        TestKnownIssuesAnalysis testKnownIssuesAnalysis,
        CancellationToken cancellationToken)
    {
        IOrderedEnumerable<TestCaseResult> uniqueFailingTestCaseResults = failingTestCaseResults
            .SelectMany(run => run.Results)
            .Distinct(new TestCaseResultNameComparer())
            .OrderBy(t => t.Name);

        var testResults = new List<TestResult>();

        // The Take(5) here is a hack to alleviate the inevitable long running cases of processing a ton of test results that may take days of processing time.
        foreach (TestCaseResult testCaseResult in uniqueFailingTestCaseResults.Take(5))
        {
            List<TestHistoryByBranch> testHistory = await _buildDataService.GetTestHistoryAsync(
                buildReference.Org,
                buildReference.Project,
                testCaseResult.Name,
                testCaseResult.CompletedDate,
                cancellationToken
            );

            var testResult = new TestResult(testCaseResult, _azdoSettings.Value.Settings.First(x => x.OrgId == buildReference.Org).CollectionUri, GetTestFailureRate(testHistory))
            {
                FailingConfigurations = BuildFailingConfiguration(buildReference, failingTestCaseResults, testCaseResult.Name),
                HelixWorkItem = await _helixDataService.TryGetHelixWorkItem(testCaseResult.Comment, cancellationToken)
            };

            testResult.IsKnownIssueFailure = testKnownIssuesAnalysis.TestResultWithKnownIssues.Any(t =>
                t.TestCaseResult.Id == testResult.TestCaseResult.Id &&
                t.TestCaseResult.TestRunId == testResult.TestCaseResult.TestRunId);
            testResults.Add(testResult);
        }

        return testResults;
    }

    private static FailureRate GetTestFailureRate(IReadOnlyList<TestHistoryByBranch> testHistory)
    {
        int failureCount = testHistory.Sum(r => r.Results.Count(x => TestOutcomeValue.Failed == x.Outcome));
        int totalCount = testHistory.Sum(r => r.Results.Count);

        return new FailureRate
        {
            FailedRuns = failureCount,
            TotalRuns = totalCount
        };
    }

    // Encapsulate construction of test result human web view
    private async Task<IReadOnlyList<TimelineRecord>> GetRecordsFromLatestPreviousAttempt(IReadOnlyCollection<TimelineRecord> latestTimelineRecords,
        BuildReferenceIdentifier buildReference, CancellationToken cancellationToken)
    {
        if (latestTimelineRecords == null || latestTimelineRecords.Count < 1) return new List<TimelineRecord>();

        int latestPreviousAttempt = latestTimelineRecords
            .SelectMany(r => r.PreviousAttempts)
            .Where(a => a != null)
            .Max(a => a.Attempt);

        List<Guid> timelineIdsPreviousAttempts = latestTimelineRecords
            .SelectMany(r => r.PreviousAttempts)
            .Where(a => a?.Attempt == latestPreviousAttempt)
            .Select(a => a.TimelineId)
            .Distinct()
            .ToList();

        _logger.LogInformation($"BuildId {buildReference.BuildId} in org {buildReference.Org} in org {buildReference.Org}in project {buildReference.Project} from attempt: {latestPreviousAttempt} with timelineIds: {string.Join(", ", timelineIdsPreviousAttempts)}");
        var previousTimelineTask = timelineIdsPreviousAttempts.Select(timelineId =>
            _buildDataService.GetBuildTimelineRecordsAsync(buildReference.Org, buildReference.Project,
                buildReference.BuildId, timelineId, cancellationToken)).ToList();

        var previousTimelineRecords = await Task.WhenAll(previousTimelineTask);
        return previousTimelineRecords.SelectMany(r => r).ToList();
    }

    private string BuildTestResultsTabUri(Build build)
    {
        return $"{_azdoSettings.Value.Settings.First(x => x.OrgId == build.OrganizationName).CollectionUri}/{build.ProjectName}/_build/results?buildId={build.Id}&view=ms.vss-test-web.build-test-results-tab";
    }

    private async Task<int> BuildAttempt(string org, string project, int buildId, CancellationToken cancellationToken)
    {
        IEnumerable<TimelineRecord> timelineRecords = await _buildDataService.GetLatestBuildTimelineRecordsAsync(
            org,
            project,
            buildId,
            cancellationToken
        );
        return timelineRecords.Any() ? timelineRecords.Max(r => r.Attempt) : 0;
    }

    private void CleanBuildMessages(List<StepResult> stepResults, List<TestRunDetails> testRunDetails, Dictionary<Guid, TimelineRecord> recordDictionary = null)
    {
        string suffixMatch = "exited with code '1'.";

        // Remove "exit code 1" iff other messages exist in the step
        foreach (StepResult stepResult in stepResults)
        {
            int errorMessages = stepResult.Errors.Count;
            int matchingErrorMessages = stepResult.Errors.Count(error => error.ErrorMessage.EndsWith(suffixMatch));

            if (errorMessages > 1)
            {
                if (matchingErrorMessages != errorMessages)
                {   // At least one message does not match suffix.
                    stepResult.Errors.RemoveAll(error => error.ErrorMessage.EndsWith(suffixMatch));
                }
            }
        }

        if (recordDictionary?.Count > 0)
        {
            foreach (StepResult stepResult in stepResults)
            {
                PipelineReference buildPipelineReference = BuildPipelineReference(Guid.Parse(stepResult.JobId), recordDictionary);
                if (buildPipelineReference != null && testRunDetails.Any(t => buildPipelineReference.Equals(t.PipelineReference)))
                {
                    stepResult.Errors.RemoveAll(issue => IsTestError(issue.ErrorMessage));
                }
            }
        }

        // Remove "NETCORE_ENGINEERING_TELEMETRY" marks
        foreach (StepResult stepResult in stepResults)
        {
            foreach (Error error in stepResult.Errors)
            {
                int startIndex = error.ErrorMessage.IndexOf("(NETCORE_ENGINEERING_TELEMETRY=");

                if (startIndex < 0)
                    continue;

                int endIndex = error.ErrorMessage.IndexOf(')', startIndex);

                if (error.ErrorMessage.Length - 1 > endIndex && error.ErrorMessage[endIndex + 1] == ' ')
                {
                    endIndex += 1;
                }

                System.Text.StringBuilder newErrorMessage = new System.Text.StringBuilder(error.ErrorMessage.Length);

                newErrorMessage.Append(error.ErrorMessage[..startIndex]);
                newErrorMessage.Append(error.ErrorMessage[(endIndex + 1)..]);

                error.ErrorMessage = newErrorMessage.ToString();
            }
        }
        stepResults.RemoveAll(t => t.Errors.Count == 0 && t.KnownIssues.Count == 0);
    }

    private static bool IsTestError(string errorMessage)
    {
        return errorMessage.Contains("(NETCORE_ENGINEERING_TELEMETRY=Test)") ||
               errorMessage.StartsWith("Vstest") ||
               errorMessage.StartsWith("Test Run Failed");
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis;
using BuildInsights.BuildAnalysis.WorkItems.Models;
using BuildInsights.GitHub;
using BuildInsights.GitHub.Models;
using BuildInsights.GitHubGraphQL;
using BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebHooks;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
using ProductConstructionService.WorkItems;

#nullable disable
namespace BuildInsights.Api.Controllers;

[Route("github/webhooks")]
public class GitHubWebhooksController : ControllerBase
{
    private const string GitHubAppName = "Build Insights";

    private readonly IGitHubChecksService _checks;
    private readonly IGitHubGraphQLClient _graphQLClient;
    private readonly OperationManager _operations;
    private readonly IOptionsMonitor<KnownIssuesProjectUpdatingOptions> _knownIssuesProjectBoardOptions;
    private readonly IOptionsMonitor<GitHubAppSettings> _appSettings;
    private readonly ILogger<GitHubWebhooksController> _logger;
    private readonly IGitHubPullRequestService _prService;
    private readonly IWorkItemProducerFactory _workItemProducerFactory;

    public GitHubWebhooksController(
        IGitHubChecksService checks,
        IGitHubGraphQLClient graphQLClient,
        OperationManager operations,
        IOptionsMonitor<KnownIssuesProjectUpdatingOptions> knownIssuesProjectBoardOptions,
        IOptionsMonitor<GitHubAppSettings> appSettings,
        IGitHubPullRequestService prService,
        IWorkItemProducerFactory workItemProducerFactory,
        ILogger<GitHubWebhooksController> logger)
    {
        _checks = checks;
        _graphQLClient = graphQLClient;
        _operations = operations;
        _knownIssuesProjectBoardOptions = knownIssuesProjectBoardOptions;
        _appSettings = appSettings;
        _prService = prService;
        _workItemProducerFactory = workItemProducerFactory;
        _logger = logger;
    }

    [GitHubWebHook(EventName = "pull_request")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PullRequestHandler(JObject data)
    {
        using Operation scope = CreateGitHubHookLoggingScope("check_suite");

        var pullRequest = PullRequestGitHubEventWorkItem.Parse(data);
        _logger.LogInformation(
            "Received event: 'pull_request', action: {action}, organization: {organization}, repository: {repository}, commit: {commit}, pr: {pr} ",
            pullRequest.Action, pullRequest.Organization, pullRequest.Repository, pullRequest.HeadSha, pullRequest.Number);

        switch (pullRequest.Action)
        {
            case "opened":
            case "synchronize":
            case "closed":
                if (IsBuildAnalysisEvent())
                {
                    var producer = _workItemProducerFactory.CreateProducer<PullRequestGitHubEventWorkItem>();
                    await producer.ProduceWorkItemAsync(pullRequest);
                }

                return NoContent();
            default:
                _logger.LogInformation(
                    "No action taken for event: 'pull_request', action: {action}, organization: {organization}, repository: {repository}, commit: {commit}, pr: {pr} ",
                    pullRequest.Action, pullRequest.Organization, pullRequest.Repository, pullRequest.HeadSha, pullRequest.Number);
                return NoContent();
        }
    }

    [GitHubWebHook(EventName = "check_run")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CheckRunHandler(JObject data)
    {
        using Operation scope = CreateGitHubHookLoggingScope("check_run");

        var checkRun = CheckRunData.Parse(data);
        CheckSuiteData checkSuite = checkRun.CheckSuite;
        string applicationId = GetApplicationId();

        _logger.LogInformation("Received event: 'check_run', action: {action}, organization: {organization}, repository: {repository}, commit: {commit}",
            checkSuite.Action, checkSuite.Organization, checkSuite.Repository, checkSuite.HeadSha);

        switch (checkSuite.Action)
        {
            case "rerequested":
                var producer = _workItemProducerFactory.CreateProducer<CheckRunRerunGitHubEvent>();

                if (applicationId.Equals(_appSettings.Get(GitHubAppNames.NetHelix).AppId) &&
                    checkRun.Name.Equals("Build Analysis")) // TODO - Wrong name
                {
                    await PostBuildAnalysisDeprecatedMessage(checkSuite);
                    await producer.ProduceWorkItemAsync(new CheckRunRerunGitHubEvent()
                    {
                        Repository = checkRun.CheckSuite.Repository,
                        HeadSha = checkRun.CheckSuite.HeadSha,
                    });
                }
                else if (IsBuildAnalysisEvent())
                {
                    await producer.ProduceWorkItemAsync(new CheckRunRerunGitHubEvent()
                    {
                        Repository = checkRun.CheckSuite.Repository,
                        HeadSha = checkRun.CheckSuite.HeadSha,
                    });
                }

                return NoContent();

            default:
                _logger.LogInformation(
                    "No action taken for event: 'check_run', action: {action}, organization: {organization}, repository: {repository}, commit: {commit} ",
                    checkSuite.Action, checkSuite.Organization, checkSuite.Repository, checkSuite.HeadSha);
                return NoContent();
        }
    }

    [GitHubWebHook(EventName = "issues")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> IssueHandler(JObject data)
    {
        using Operation scope = CreateGitHubHookLoggingScope("issues");
        var issue = IssuesHookData.Parse(data);

        _logger.LogInformation(
            "Processing event: 'issues', action: {action}, organization: {organization}, repository: {repository}, issue: {number}",
            issue.Action, issue.Organization, issue.Repository, issue.IssueNumber);

        if (IsBuildAnalysisEvent()
            && issue.SenderType != Octokit.AccountType.Bot.ToString()
            && _knownIssuesProjectBoardOptions.CurrentValue.KnownIssueLabels.Intersect(issue.Labels).Any()
            && new[] { "opened", "reopened", "labeled", "edited" }.Contains(issue.Action)
            && (issue.Action != "labeled" || _knownIssuesProjectBoardOptions.CurrentValue.KnownIssueLabels.Contains(issue.AddedLabel)))
        {
            var queueProducer = _workItemProducerFactory.CreateProducer<KnownIssueAnalysisRequest>();

            _logger.LogInformation("Requesting known issues analysis for issue {organization}/{issueRepo}#{issueId}", issue.Organization, issue.Repository, issue.IssueNumber);

            await queueProducer.ProduceWorkItemAsync(
                new KnownIssueAnalysisRequest
                {
                    IssueId = issue.IssueNumber,
                    Repository = issue.Organization + "/" + issue.Repository
                });

            if (issue.Action != "reopened")
            {
                _logger.LogInformation("Sending validation request for {organization}/{repository}#{issueNumber}", issue.Organization, issue.Repository, issue.IssueNumber);

                var producer = _workItemProducerFactory.CreateProducer<KnownIssueValidationRequest>();
                await producer.ProduceWorkItemAsync(new()
                {
                    Organization = issue.Organization,
                    Repository = issue.Repository,
                    IssueId = issue.IssueNumber,
                    RepositoryWithOwner = $"{issue.Organization}/{issue.Repository}",
                });
            }
        }

        IEnumerable<string> knownIssuesLabels = _knownIssuesProjectBoardOptions.CurrentValue.KnownIssueLabels.Union(_knownIssuesProjectBoardOptions.CurrentValue.CriticalIssueLabels);

        switch (issue.Action)
        {
            case "labeled":
                var labeled = LabelData.Parse(data);
                if (issue.Organization.Equals(_knownIssuesProjectBoardOptions.CurrentValue.Organization, StringComparison.OrdinalIgnoreCase) &&
                    knownIssuesLabels.Contains(labeled.Name))
                {
                    GitHubGraphQLProjectV2 knownIssuesProject = await _graphQLClient.GetProjectForOrganization(
                        _knownIssuesProjectBoardOptions.CurrentValue.Organization,
                        _knownIssuesProjectBoardOptions.CurrentValue.ProjectNumber);

                    GitHubGraphQLProjectV2Item projectItem = await AddOrGetProjectIssue(knownIssuesProject, issue);
                    await UpdateProjectItemIssueField(knownIssuesProject, projectItem,
                        _knownIssuesProjectBoardOptions.CurrentValue.IssueTypeField, labeled.Name);
                }

                return NoContent();
            case "unlabeled":
                var unlabeled = LabelData.Parse(data);
                if (issue.Organization.Equals(_knownIssuesProjectBoardOptions.CurrentValue.Organization, StringComparison.OrdinalIgnoreCase) &&
                    !issue.Labels.Intersect(knownIssuesLabels).Any())
                {

                    GitHubGraphQLProjectV2 knownIssuesProject = await _graphQLClient.GetProjectForOrganization(
                        _knownIssuesProjectBoardOptions.CurrentValue.Organization,
                        _knownIssuesProjectBoardOptions.CurrentValue.ProjectNumber);

                    await DeleteIssueFromProject(issue, knownIssuesProject.Id);
                }

                return NoContent();
            default:
                _logger.LogInformation(
                    "No action taken for event: 'issues', action: {action}, organization: {organization}, repository: {repository}, issue: {number}",
                    issue.Action, issue.Organization, issue.Repository, issue.IssueNumber);
                return NoContent();
        }
    }

    [GitHubWebHook(EventName = "issue_comment")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> IssueCommentHandler(JObject data)
    {
        using Operation scope = CreateGitHubHookLoggingScope("issue_comment");
        var comment = IssueCommentData.Parse(data);

        // validate comment - contains data, this is from a PR, starts with specific command
        if (IsBuildAnalysisEvent()
            && comment.Action.Equals("created", StringComparison.CurrentCultureIgnoreCase)
            && comment.IsPullRequestComment
            && !string.IsNullOrWhiteSpace(comment.Comment)
            && comment.Comment.StartsWith(BuildAnalysisEscapeMechanismHelper.ChangeToGreenCommand, StringComparison.InvariantCultureIgnoreCase))
        {
            _logger.LogInformation("Processing a change-to-green command for PR {repository}/pulls/{prNumber}",
                comment.Repository,
                comment.IssueNumber);

            string justification = comment.Comment.Substring(BuildAnalysisEscapeMechanismHelper.ChangeToGreenCommand.Length).Trim();

            Octokit.PullRequest pullRequest = await _prService.GetPullRequest(comment.Repository, comment.IssueNumber);
            string currentHeadSha = pullRequest.Head?.Sha;

            var producer = _workItemProducerFactory.CreateProducer<CheckRunConclusionUpdateEvent>();
            await producer.ProduceWorkItemAsync(new CheckRunConclusionUpdateEvent
            {
                Repository = comment.Repository,
                IssueNumber = comment.IssueNumber,
                HeadSha = currentHeadSha,
                Justification = justification,
                CheckResultString = Octokit.CheckConclusion.Success.ToString(),
            });
        }

        return NoContent();
    }

    private async Task<GitHubGraphQLProjectV2Item> AddOrGetProjectIssue(GitHubGraphQLProjectV2 project, IssuesHookData issue)
    {
        GitHubGraphQLProjectV2Item projectItem = await _graphQLClient.AddOrGetProjectIssue(project.Id, issue.IssueNodeId);
        _logger.LogInformation("Added issue {organization}/{repository}#{number} to {board}. Board id for issue {itemId}",
            issue.Organization, issue.Repository, issue.IssueNumber, project.Title, projectItem.Id);

        return projectItem;
    }

    private async Task UpdateProjectItemIssueField(GitHubGraphQLProjectV2 project, GitHubGraphQLProjectV2Item projectItem, string fieldName, string fieldValue)
    {
        GitHubGraphQLField field = await _graphQLClient.GetField(fieldName, project.Id);
        await _graphQLClient.UpdateIssueTextField(project.Id, projectItem.Id, field.Id, fieldValue);

        _logger.LogInformation("Updated field {fieldName} with value: {fieldValue} to project item on {board}.",
            fieldName, fieldValue, project.Title);
    }

    private async Task DeleteIssueFromProject(IssuesHookData issue, string knownIssuesProjectId)
    {
        string issueProjectItemId = await _graphQLClient.TryGetIssueProjectItem(
            issue.Repository, issue.Organization, issue.IssueNumber,
            knownIssuesProjectId);

        if (!string.IsNullOrEmpty(issueProjectItemId))
        {
            await _graphQLClient.DeleteProjectIssue(knownIssuesProjectId, issueProjectItemId);
        }
    }

    private Operation CreateGitHubHookLoggingScope(string eventName)
    {
        if (!Request.Headers.TryGetValue("X-GitHub-Delivery", out StringValues deliveryId))
        {
            deliveryId = "MISSING";
        }

        if (!Request.Headers.TryGetValue("X-GitHub-Hook-Installation-Target-ID", out var targetInstallationId))
        {
            targetInstallationId = "MISSING";
        }

        return _operations.BeginLoggingScope(
            "GitHub Hook, event: '{GitHubEvent}', target-installation: {GitHubTargetInstallation}, delivery: {GitHubDeliveryId}",
            eventName,
            targetInstallationId.ToString(),
            deliveryId.ToString()
        );
    }

    private async Task PostBuildAnalysisDeprecatedMessage(CheckSuiteData checkSuite)
    {
        await _checks.PostChecksResultAsync(
            "Build Analysis",
            ".NET Result Analysis",
            "## :warning: Build Analysis has been removed from this application (.Net Helix). " +
            "Now available on Build Analysis App",
            checkSuite.Repository,
            checkSuite.HeadSha,
            CheckResult.Passed,
            CancellationToken.None
        );
    }

    private string GetApplicationId()
    {
        if (!Request.Headers.TryGetValue("X-GitHub-Hook-Installation-Target-ID", out var targetInstallationId))
        {
            targetInstallationId = "NOT FOUND";
        }

        return targetInstallationId;
    }

    private bool IsBuildAnalysisEvent()
    {
        string applicationId = GetApplicationId();
        return applicationId.Equals(_appSettings.Get(GitHubAppName).AppId); // TODO
    }

    [GitHubWebHook]
    [IgnoreAntiforgeryToken]
    public IActionResult GitHubHandler(string id, string @event, JObject data)
    {
        _logger.LogDebug("Received unhandled event {eventName}", @event);
        return NoContent();
    }

    private record CheckRunData(
        string Action,
        string CheckRunId,
        string Name,
        string Status,
        string Conclusion,
        CheckSuiteData CheckSuite)
    {
        public static CheckRunData Parse(JObject data)
            => new(
                data.Value<string>("action"),
                data.Value<JObject>("check_run").Value<string>("id"),
                data.Value<JObject>("check_run").Value<string>("name"),
                data.Value<JObject>("check_run").Value<string>("status"),
                data.Value<JObject>("check_run").Value<string>("conclusion"),
                CheckSuiteData.Parse(data));
    }

    private record CheckSuiteData(
        string Action,
        string Organization,
        string Repository,
        string HeadSha)
    {
        public static CheckSuiteData Parse(JObject data)
            => new(
                data.Value<string>("action"),
                data.Value<JObject>("organization")?.Value<string>("login") ??
                data.Value<JObject>("repository").Value<JObject>("owner").Value<string>("login"),
                data.Value<JObject>("repository").Value<string>("full_name"),
                data.Value<JObject>("check_run").Value<string>("head_sha"));
    }

    private record IssueCommentData(
        string Action,
        string IssueNodeId,
        int IssueNumber,
        string Organization,
        string Repository,
        string Comment,
        bool IsPullRequestComment)
    {
        public static IssueCommentData Parse(JObject data)
            => new(
                data.Value<string>("action"),
                data.Value<JObject>("issue").Value<string>("node_id"),
                data.Value<JObject>("issue").Value<int>("number"),
                data.Value<JObject>("repository").Value<JObject>("owner").Value<string>("login"),
                data.Value<JObject>("repository").Value<string>("full_name"),
                data.Value<JObject>("comment").Value<string>("body"),
                data.Value<JObject>("issue").Value<JObject>("pull_request") != null);
    }

    private record IssuesHookData(
        string Action,
        string IssueNodeId,
        long IssueNumber,
        string Organization,
        string Repository,
        List<string> Labels,
        string AddedLabel,
        string SenderId,
        string SenderType)
    {
        public static IssuesHookData Parse(JObject data)
            => new(
                data.Value<string>("action"),
                data.Value<JObject>("issue").Value<string>("node_id"),
                data.Value<JObject>("issue").Value<long>("number"),
                data.Value<JObject>("repository").Value<JObject>("owner").Value<string>("login"),
                data.Value<JObject>("repository").Value<string>("name"),
                data.Value<JObject>("issue").Value<JArray>("labels").Values<string>("name").ToList(),
                data.Value<JObject>("label")?.Value<string>("name") ?? null,
                data.Value<JObject>("sender")?.Value<string>("id"),
                data.Value<JObject>("sender")?.Value<string>("type"));
    }

    private record LabelData(string Name)
    {
        public static LabelData Parse(JObject data)
            => new(data.Value<JObject>("label").Value<string>("name"));
    }
}

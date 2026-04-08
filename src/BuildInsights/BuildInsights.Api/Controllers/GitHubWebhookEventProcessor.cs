// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis;
using BuildInsights.BuildAnalysis.WorkItems.Models;
using BuildInsights.GitHub;
using BuildInsights.GitHub.Models;
using BuildInsights.GitHubGraphQL;
using BuildInsights.GitHubGraphQL.GitHubGraphQLAPI;
using BuildInsights.KnownIssues.Models;
using BuildInsights.ServiceDefaults.Configuration.Models;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.Extensions.Options;
using Octokit.Webhooks;
using Octokit.Webhooks.Events;
using Octokit.Webhooks.Events.CheckRun;
using Octokit.Webhooks.Events.IssueComment;
using Octokit.Webhooks.Events.Issues;
using Octokit.Webhooks.Events.PullRequest;
using ProductConstructionService.WorkItems;

#nullable disable
namespace BuildInsights.Api.Controllers;

public class GitHubWebhookEventProcessor : WebhookEventProcessor
{
    private readonly IGitHubChecksService _checks;
    private readonly IGitHubGraphQLClient _graphQLClient;
    private readonly OperationManager _operations;
    private readonly IOptionsMonitor<KnownIssuesProjectOptions> _knownIssuesProjectBoardOptions;
    private readonly IOptionsMonitor<GitHubAppSettings> _appSettings;
    private readonly ILogger<GitHubWebhookEventProcessor> _logger;
    private readonly IGitHubPullRequestService _prService;
    private readonly IWorkItemProducerFactory _workItemProducerFactory;

    public GitHubWebhookEventProcessor(
        IGitHubChecksService checks,
        IGitHubGraphQLClient graphQLClient,
        OperationManager operations,
        IOptionsMonitor<KnownIssuesProjectOptions> knownIssuesProjectBoardOptions,
        IOptionsMonitor<GitHubAppSettings> appSettings,
        IGitHubPullRequestService prService,
        IWorkItemProducerFactory workItemProducerFactory,
        ILogger<GitHubWebhookEventProcessor> logger)
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

    protected override async Task ProcessPullRequestWebhookAsync(
        WebhookHeaders headers,
        PullRequestEvent pullRequestEvent,
        PullRequestAction action)
    {
        using Operation scope = CreateGitHubHookLoggingScope(headers, "pull_request");

        string organization = pullRequestEvent.Organization?.Login
            ?? pullRequestEvent.Repository.Owner.Login;
        string repository = pullRequestEvent.Repository.Name;
        string headSha = pullRequestEvent.PullRequest.Head.Sha;
        long number = pullRequestEvent.Number;

        _logger.LogInformation(
            "Received event: 'pull_request', action: {action}, organization: {organization}, repository: {repository}, commit: {commit}, pr: {pr} ",
            pullRequestEvent.Action, organization, repository, headSha, number);

        switch (pullRequestEvent.Action)
        {
            case "opened":
            case "synchronize":
            case "closed":
                if (IsBuildAnalysisEvent(headers))
                {
                    var producer = _workItemProducerFactory.CreateProducer<PullRequestGitHubEventWorkItem>();
                    await producer.ProduceWorkItemAsync(new PullRequestGitHubEventWorkItem
                    {
                        Action = pullRequestEvent.Action,
                        Merged = pullRequestEvent.PullRequest.Merged ?? false,
                        Organization = organization,
                        Repository = repository,
                        HeadSha = headSha,
                        Number = number,
                    });
                }

                break;
            default:
                _logger.LogInformation(
                    "No action taken for event: 'pull_request', action: {action}, organization: {organization}, repository: {repository}, commit: {commit}, pr: {pr} ",
                    pullRequestEvent.Action, organization, repository, headSha, number);
                break;
        }
    }

    protected override async Task ProcessCheckRunWebhookAsync(
        WebhookHeaders headers,
        CheckRunEvent checkRunEvent,
        CheckRunAction action)
    {
        using Operation scope = CreateGitHubHookLoggingScope(headers, "check_run");

        string organization = checkRunEvent.Organization?.Login
            ?? checkRunEvent.Repository.Owner.Login;
        string repository = checkRunEvent.Repository.FullName;
        string headSha = checkRunEvent.CheckRun.HeadSha;
        string applicationId = headers.HookInstallationTargetId ?? "NOT FOUND";

        _logger.LogInformation("Received event: 'check_run', action: {action}, organization: {organization}, repository: {repository}, commit: {commit}",
            checkRunEvent.Action, organization, repository, headSha);

        switch (checkRunEvent.Action)
        {
            case "rerequested":
                var producer = _workItemProducerFactory.CreateProducer<CheckRunRerunGitHubEvent>();

                if (applicationId.Equals(_appSettings.CurrentValue.AppId.ToString())
                    && checkRunEvent.CheckRun.Name.Equals(_appSettings.CurrentValue.AppName))
                {
                    await PostBuildAnalysisDeprecatedMessage(repository, headSha);
                    await producer.ProduceWorkItemAsync(new CheckRunRerunGitHubEvent
                    {
                        Repository = repository,
                        HeadSha = headSha,
                    });
                }
                else if (IsBuildAnalysisEvent(headers))
                {
                    await producer.ProduceWorkItemAsync(new CheckRunRerunGitHubEvent
                    {
                        Repository = repository,
                        HeadSha = headSha,
                    });
                }

                break;

            default:
                _logger.LogInformation(
                    "No action taken for event: 'check_run', action: {action}, organization: {organization}, repository: {repository}, commit: {commit} ",
                    checkRunEvent.Action, organization, repository, headSha);
                break;
        }
    }

    protected override async Task ProcessIssuesWebhookAsync(
        WebhookHeaders headers,
        IssuesEvent issuesEvent,
        IssuesAction action)
    {
        using Operation scope = CreateGitHubHookLoggingScope(headers, "issues");

        string organization = issuesEvent.Repository.Owner.Login;
        string repository = issuesEvent.Repository.Name;
        long issueNumber = issuesEvent.Issue.Number;
        string issueNodeId = issuesEvent.Issue.NodeId;
        List<string> labels = issuesEvent.Issue.Labels.Select(l => l.Name).ToList();
        string addedLabel = issuesEvent is IssuesLabeledEvent labeled ? labeled.Label?.Name : null;
        string senderType = issuesEvent.Sender?.Type?.ToString();

        _logger.LogInformation(
            "Processing event: 'issues', action: {action}, organization: {organization}, repository: {repository}, issue: {number}",
            issuesEvent.Action, organization, repository, issueNumber);

        if (IsBuildAnalysisEvent(headers)
            && senderType != Octokit.AccountType.Bot.ToString()
            && _knownIssuesProjectBoardOptions.CurrentValue.KnownIssueLabels.Intersect(labels).Any()
            && new[] { "opened", "reopened", "labeled", "edited" }.Contains(issuesEvent.Action)
            && (issuesEvent.Action != "labeled" || _knownIssuesProjectBoardOptions.CurrentValue.KnownIssueLabels.Contains(addedLabel)))
        {
            var queueProducer = _workItemProducerFactory.CreateProducer<KnownIssueAnalysisRequest>();

            _logger.LogInformation("Requesting known issues analysis for issue {organization}/{issueRepo}#{issueId}", organization, repository, issueNumber);

            await queueProducer.ProduceWorkItemAsync(
                new KnownIssueAnalysisRequest
                {
                    IssueId = issueNumber,
                    Repository = organization + "/" + repository
                });

            if (issuesEvent.Action != "reopened")
            {
                _logger.LogInformation("Sending validation request for {organization}/{repository}#{issueNumber}", organization, repository, issueNumber);

                var validationProducer = _workItemProducerFactory.CreateProducer<KnownIssueValidationRequest>();
                await validationProducer.ProduceWorkItemAsync(new()
                {
                    Organization = organization,
                    Repository = repository,
                    IssueId = issueNumber,
                    RepositoryWithOwner = $"{organization}/{repository}",
                });
            }
        }

        IEnumerable<string> knownIssuesLabels = _knownIssuesProjectBoardOptions.CurrentValue.KnownIssueLabels
            .Union(_knownIssuesProjectBoardOptions.CurrentValue.CriticalIssueLabels);

        switch (issuesEvent.Action)
        {
            case "labeled":
                string labeledName = (issuesEvent as IssuesLabeledEvent)?.Label?.Name;
                if (organization.Equals(_knownIssuesProjectBoardOptions.CurrentValue.Organization, StringComparison.OrdinalIgnoreCase) &&
                    knownIssuesLabels.Contains(labeledName))
                {
                    GitHubGraphQLProjectV2 knownIssuesProject = await _graphQLClient.GetProjectForOrganization(
                        _knownIssuesProjectBoardOptions.CurrentValue.Organization,
                        _knownIssuesProjectBoardOptions.CurrentValue.ProjectNumber);

                    GitHubGraphQLProjectV2Item projectItem = await AddOrGetProjectIssue(
                        knownIssuesProject, organization, repository, issueNumber, issueNodeId);
                    await UpdateProjectItemIssueField(knownIssuesProject, projectItem,
                        _knownIssuesProjectBoardOptions.CurrentValue.IssueTypeField, labeledName);
                }

                break;
            case "unlabeled":
                if (organization.Equals(_knownIssuesProjectBoardOptions.CurrentValue.Organization, StringComparison.OrdinalIgnoreCase) &&
                    !labels.Intersect(knownIssuesLabels).Any())
                {
                    GitHubGraphQLProjectV2 knownIssuesProject = await _graphQLClient.GetProjectForOrganization(
                        _knownIssuesProjectBoardOptions.CurrentValue.Organization,
                        _knownIssuesProjectBoardOptions.CurrentValue.ProjectNumber);

                    await DeleteIssueFromProject(repository, organization, issueNumber, knownIssuesProject.Id);
                }

                break;
            default:
                _logger.LogInformation(
                    "No action taken for event: 'issues', action: {action}, organization: {organization}, repository: {repository}, issue: {number}",
                    issuesEvent.Action, organization, repository, issueNumber);
                break;
        }
    }

    protected override async Task ProcessIssueCommentWebhookAsync(
        WebhookHeaders headers,
        IssueCommentEvent issueCommentEvent,
        IssueCommentAction action)
    {
        _logger.LogInformation($"Processing issue comment webhook: {issueCommentEvent.Comment.Body}");

        if (!IsBuildAnalysisEvent(headers))
        {
            return;
        }

        if (issueCommentEvent.Action != IssueCommentActionValue.Created)
        {
            return;
        }

        if (issueCommentEvent.Issue.PullRequest == null)
        {
            return;
        }

        string comment = issueCommentEvent.Comment.Body.Trim();

        if (!comment.StartsWith(BuildAnalysisEscapeMechanismHelper.ChangeToGreenCommand, StringComparison.InvariantCultureIgnoreCase))
        {
            return;
        }

        using Operation scope = CreateGitHubHookLoggingScope(headers, "issue_comment");

        string repository = issueCommentEvent.Repository.FullName;
        int issueNumber = (int)issueCommentEvent.Issue.Number;

        _logger.LogInformation("Received a change-to-green command event for PR {repository}/pulls/{prNumber}",
            repository,
            issueNumber);

        string justification = comment.Substring(BuildAnalysisEscapeMechanismHelper.ChangeToGreenCommand.Length).Trim();

        Octokit.PullRequest pullRequest = await _prService.GetPullRequest(repository, issueNumber);
        string currentHeadSha = pullRequest.Head?.Sha;

        var producer = _workItemProducerFactory.CreateProducer<CheckRunConclusionUpdateEvent>();
        await producer.ProduceWorkItemAsync(new CheckRunConclusionUpdateEvent
        {
            Repository = repository,
            IssueNumber = issueNumber,
            HeadSha = currentHeadSha,
            Justification = justification,
            CheckResultString = Octokit.CheckConclusion.Success.ToString(),
        });
    }

    private async Task<GitHubGraphQLProjectV2Item> AddOrGetProjectIssue(
        GitHubGraphQLProjectV2 project,
        string organization,
        string repository,
        long issueNumber,
        string issueNodeId)
    {
        GitHubGraphQLProjectV2Item projectItem = await _graphQLClient.AddOrGetProjectIssue(project.Id, issueNodeId);
        _logger.LogInformation("Added issue {organization}/{repository}#{number} to {board}. Board id for issue {itemId}",
            organization, repository, issueNumber, project.Title, projectItem.Id);

        return projectItem;
    }

    private async Task UpdateProjectItemIssueField(
        GitHubGraphQLProjectV2 project,
        GitHubGraphQLProjectV2Item projectItem,
        string fieldName,
        string fieldValue)
    {
        GitHubGraphQLField field = await _graphQLClient.GetField(fieldName, project.Id);
        await _graphQLClient.UpdateIssueTextField(project.Id, projectItem.Id, field.Id, fieldValue);

        _logger.LogInformation("Updated field {fieldName} with value: {fieldValue} to project item on {board}.",
            fieldName, fieldValue, project.Title);
    }

    private async Task DeleteIssueFromProject(string repository, string organization, long issueNumber, string knownIssuesProjectId)
    {
        string issueProjectItemId = await _graphQLClient.TryGetIssueProjectItem(
            repository, organization, issueNumber,
            knownIssuesProjectId);

        if (!string.IsNullOrEmpty(issueProjectItemId))
        {
            await _graphQLClient.DeleteProjectIssue(knownIssuesProjectId, issueProjectItemId);
        }
    }

    private Operation CreateGitHubHookLoggingScope(WebhookHeaders headers, string eventName)
    {
        string deliveryId = headers.Delivery ?? "MISSING";
        string targetInstallationId = headers.HookInstallationTargetId ?? "MISSING";

        return _operations.BeginLoggingScope(
            "GitHub Hook, event: '{GitHubEvent}', target-installation: {GitHubTargetInstallation}, delivery: {GitHubDeliveryId}",
            eventName,
            targetInstallationId,
            deliveryId);
    }

    private async Task PostBuildAnalysisDeprecatedMessage(string repository, string headSha)
    {
        await _checks.PostChecksResultAsync(
            BuildAnalysisConstants.GitHubCheckName,
            BuildAnalysisConstants.GitHubCheckOutputName,
            "## :warning: Build Analysis has been removed from this application (.Net Helix). " +
            "Now available on Build Analysis App",
            repository,
            headSha,
            CheckResult.Passed,
            CancellationToken.None);
    }

    private bool IsBuildAnalysisEvent(WebhookHeaders headers)
    {
        string applicationId = headers.HookInstallationTargetId ?? "NOT FOUND";
        return applicationId.Equals(_appSettings.CurrentValue.AppId.ToString());
    }
}

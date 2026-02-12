// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Services;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;
using Octokit;
using BuildInsights.Data.Models;
using Maestro.Common;

namespace BuildInsights.BuildAnalysis.Providers;

public class KnownIssueValidationProvider : IKnownIssueValidationService
{
    private readonly IBuildDataService _build;
    private readonly IBuildAnalysisService _buildAnalysisService;
    private readonly IGitHubIssuesService _gitHubIssuesService;
    private readonly IKnownIssuesHistoryService _knownIssuesHistoryService;
    private readonly IContextualStorage _contextualStorage;
    private readonly ILogger<KnownIssueValidationProvider> _logger;

    public KnownIssueValidationProvider(
        IBuildDataService build,
        IBuildAnalysisService buildAnalysisService,
        IGitHubIssuesService gitHubIssuesService,
        IKnownIssuesHistoryService knownIssuesHistoryService,
        IContextualStorage contextualStorage,
        ILogger<KnownIssueValidationProvider> logger)
    {
        _buildAnalysisService = buildAnalysisService;
        _gitHubIssuesService = gitHubIssuesService;
        _knownIssuesHistoryService = knownIssuesHistoryService;
        _contextualStorage = contextualStorage;
        _build = build;
        _logger = logger;
    }

    public async Task ValidateKnownIssue(KnownIssueValidationMessage knownIssueValidationMessage, CancellationToken cancellationToken)
    {
        Issue issue = await _gitHubIssuesService.GetIssueAsync(knownIssueValidationMessage.RepositoryWithOwner, knownIssueValidationMessage.IssueId);
        if (!TryGetKnownIssue(issue, knownIssueValidationMessage.RepositoryWithOwner, out KnownIssue knownIssue, out string exceptionMessage))
        {
            await UpdateIssueWithValidationResult(KnownIssueValidationResult.UnableToCreateKnownIssue(exceptionMessage), knownIssueValidationMessage);
        }

        if (knownIssue?.BuildError == null || knownIssue.BuildError.Count == 0) 
        {
            _logger.LogInformation("BuildError is not present in known issue; there is nothing to validate in GitHub Issue: {repository}#{issueId}", knownIssueValidationMessage.RepositoryWithOwner, knownIssueValidationMessage.IssueId);
            return;
        }

        BuildFromGitHubIssue buildFromGitHubIssue = await GetBuildFromBody(knownIssue.GitHubIssue.Body);
        if (buildFromGitHubIssue == null)
        {
            _logger.LogInformation("Validation of known issue was not done on GitHub Issue ({repository}#{issueId}) because it lacks a valid build.", knownIssueValidationMessage.RepositoryWithOwner, knownIssueValidationMessage.IssueId);
            await UpdateIssueWithValidationResult(KnownIssueValidationResult.MissingBuild, knownIssueValidationMessage);
            return;
        }

        List<KnownIssueAnalysis> previousKnownIssuesAnalysis = await _knownIssuesHistoryService.GetBuildKnownIssueValidatedRecords(
                buildFromGitHubIssue.Id.ToString(), knownIssueValidationMessage.RepositoryWithOwner, knownIssueValidationMessage.IssueId, cancellationToken);
        if (previousKnownIssuesAnalysis.Any(t => t.ErrorMessage.Equals(KnownIssueHelper.GetKnownIssueErrorMessageStringConversion(knownIssue.BuildError))))
        {
            _logger.LogInformation("GitHub issue: {repository}#{issueId} validation was skipped because it was previously analyzed", knownIssueValidationMessage.RepositoryWithOwner, knownIssueValidationMessage.IssueId);
            return;
        }

        _contextualStorage.SetContext($"validate/{knownIssueValidationMessage.RepositoryWithOwner}/{knownIssueValidationMessage.IssueId}");

        List<KnownIssue> knownIssues;
        try
        {
            knownIssues = await GetKnownIssuesInBuild(buildFromGitHubIssue, cancellationToken);
        }
        catch (BuildNotFoundException)
        {
            await UpdateIssueWithValidationResult(KnownIssueValidationResult.BuildNotFound, knownIssueValidationMessage);
            return;
        }
        catch (HttpRequestException rex) when(rex.StatusCode == HttpStatusCode.NotFound)
        {
            await UpdateIssueWithValidationResult(KnownIssueValidationResult.BuildInformationNotFound, knownIssueValidationMessage);
            return;
        }

        bool isMatch = knownIssues.Any(t => t.GitHubIssue.Id == knownIssue.GitHubIssue.Id && t.GitHubIssue.RepositoryWithOwner == knownIssue.GitHubIssue.RepositoryWithOwner);
        KnownIssueValidationResult knownIssueValidationResult = isMatch ? KnownIssueValidationResult.Matched : KnownIssueValidationResult.NotMatched;

        _logger.LogInformation("Updating Github issue: {repository}#{issueId} with validation result: {result}", knownIssueValidationMessage.RepositoryWithOwner, knownIssueValidationMessage.IssueId, knownIssueValidationResult.Value);
        await UpdateIssueWithValidationResult(knownIssueValidationResult, knownIssueValidationMessage, BuildUrlUtils.GetBuildUrl(buildFromGitHubIssue.OrganizationId, buildFromGitHubIssue.ProjectId,
                buildFromGitHubIssue.Id), knownIssue.BuildError);

        await _knownIssuesHistoryService.SaveBuildKnownIssueValidation(buildFromGitHubIssue.Id, knownIssueValidationMessage.RepositoryWithOwner,
            knownIssueValidationMessage.IssueId, knownIssue.BuildError, cancellationToken);
    }

    public async Task<BuildFromGitHubIssue> GetBuildFromBody(string issueBody)
    {
        string validationBody = GetKnownIssueValidation(issueBody);
        string body = string.IsNullOrEmpty(validationBody) ? GetBodyWithoutKnownIssueReport(issueBody) : validationBody;

        //https://dev.azure.com/dnceng-public/public/_build/results?buildId=284109
        // https://dev.azure.com/dnceng-public/cbb18261-c48f-4abb-8651-8cdcb5474649/_build/results?buildId=317248
        GroupCollection regexGroups = Regex.Match(body, "dev.azure.com/([a-z]+-?[a-z]+)/(.*)/_build/.*buildId=(\\d*)").Groups;
        if (regexGroups.Count <= 3) return null;

        string organizationId = regexGroups[1].Value;
        string projectId = Regex.IsMatch(regexGroups[2].Value, "^[a-zA-Z]+$")
            ? regexGroups[2].Value : await _build.GetProjectName(organizationId, regexGroups[2].Value);
        string buildId = regexGroups[3].Value;

        return new BuildFromGitHubIssue(organizationId, projectId, int.Parse(buildId));
    }

    private async Task UpdateIssueWithValidationResult(KnownIssueValidationResult knownIssueValidationResult, KnownIssueValidationMessage knownIssueValidationMessage, string buildUrl = "", List<string> errorValidated = null)
    {
        Issue issue = await _gitHubIssuesService.GetIssueAsync(knownIssueValidationMessage.RepositoryWithOwner, knownIssueValidationMessage.IssueId);

        string validationBody = WriteValidationSection(knownIssueValidationResult, buildUrl, errorValidated);
        string body = UpdateIssueValidation(issue.Body, validationBody);

        await _gitHubIssuesService.UpdateIssueBodyAsync(knownIssueValidationMessage.RepositoryWithOwner,
            knownIssueValidationMessage.IssueId, body);
    }

    private string WriteValidationSection(KnownIssueValidationResult result, string buildUrl = "", List<string> errorValidated = null)
    {
        var validation = new StringBuilder();
        validation.AppendLine(KnownIssueHelper.StartKnownIssueValidationIdentifier);
        validation.AppendLine(" ### Known issue validation");
        validation.AppendLine($"**Build: :mag_right:** {buildUrl}");

        List<string> messagesValidated = errorValidated?.Where(e => !string.IsNullOrEmpty(e)).ToList() ?? [];
        if (messagesValidated.Count > 0)
        {
            validation.AppendLine($"**Error message validated:** `[{string.Join(" ", messagesValidated)}`]");
        }

        validation.AppendLine(result.Value);
        validation.AppendLine($"**Validation performed at:** {DateTimeOffset.UtcNow.DateTime} UTC");
        validation.Append(KnownIssueHelper.EndKnownIssueValidationIdentifier);

        return validation.ToString();
    }

    private string GetKnownIssueValidation(string body)
    {
        int startIndex = body.IndexOf(KnownIssueHelper.StartKnownIssueValidationIdentifier, StringComparison.OrdinalIgnoreCase);
        int endIndex = body.LastIndexOf(KnownIssueHelper.EndKnownIssueValidationIdentifier, StringComparison.OrdinalIgnoreCase);
        if (startIndex == -1 || endIndex == -1) return string.Empty;

        endIndex += KnownIssueHelper.EndKnownIssueValidationIdentifier.Length;
        string knownIssueValidationSection = body.Substring(startIndex, endIndex - startIndex);
        return knownIssueValidationSection;
    }


    private string GetBodyWithoutKnownIssueReport(string body)
    {
        int startIndex = body.IndexOf(KnownIssueHelper.StartKnownIssueReportIdentifier, StringComparison.OrdinalIgnoreCase);
        int endIndex = body.LastIndexOf(KnownIssueHelper.EndKnownIssueReportIdentifier, StringComparison.OrdinalIgnoreCase);

        if (startIndex < 0 || endIndex < 0) return body;

        endIndex += KnownIssueHelper.EndKnownIssueReportIdentifier.Length;
        string prevMessage = body[..startIndex];
        string endMessage = body[endIndex..];

        var bodyWithoutReport = new StringBuilder();
        bodyWithoutReport.Append(prevMessage);
        bodyWithoutReport.Append(endMessage);

        return bodyWithoutReport.ToString();
    }

    private string UpdateIssueValidation(string body, string validation)
    {
        if (string.IsNullOrEmpty(body)) return validation;

        int startIndex = body.IndexOf(KnownIssueHelper.StartKnownIssueValidationIdentifier, StringComparison.OrdinalIgnoreCase);
        int endIndex = body.LastIndexOf(KnownIssueHelper.EndKnownIssueValidationIdentifier, StringComparison.OrdinalIgnoreCase);
        startIndex = startIndex == -1 ? body.Length : startIndex;
        endIndex = endIndex == -1 ? body.Length : endIndex + KnownIssueHelper.EndKnownIssueValidationIdentifier.Length;

        string prevMessage = body[..startIndex];
        string endMessage = body[endIndex..];

        var bodyWithValidation = new StringBuilder();
        bodyWithValidation.Append(prevMessage);
        bodyWithValidation.Append(validation);
        bodyWithValidation.Append(endMessage);

        return bodyWithValidation.ToString();
    }

    private async Task<List<KnownIssue>> GetKnownIssuesInBuild(BuildFromGitHubIssue buildFromGitHubIssue, CancellationToken cancellationToken)
    {
        _logger.LogInformation("HandleMessage for buildId: {buildId} and project: {projectId} and org: {orgId}",
            buildFromGitHubIssue.Id, buildFromGitHubIssue.ProjectId, buildFromGitHubIssue.Id);

        Build build = await _build.GetBuildAsync(buildFromGitHubIssue.OrganizationId, buildFromGitHubIssue.ProjectId,
            buildFromGitHubIssue.Id, cancellationToken);

        var buildReference = new NamedBuildReference(
            build.DefinitionName,
            build.Links.Web,
            buildFromGitHubIssue.OrganizationId,
            buildFromGitHubIssue.ProjectId,
            buildFromGitHubIssue.Id,
            build.Url,
            build.DefinitionId,
            build.DefinitionName,
            build.Repository.Name,
            build.CommitHash,
            build.TargetBranch?.BranchName);

        BuildResultAnalysis buildResultAnalysis = await _buildAnalysisService.GetBuildResultAnalysisAsync(buildReference, cancellationToken, true);
        List<KnownIssue> knownIssues = buildResultAnalysis.BuildStepsResult.SelectMany(t => t.KnownIssues).ToList();
        knownIssues.AddRange(buildResultAnalysis.TestKnownIssuesAnalysis.TestResultWithKnownIssues.SelectMany(t => t.KnownIssues).ToList());

        return knownIssues;
    }

    private bool TryGetKnownIssue(Issue issue, string repositoryWithOwner, out KnownIssue knownIssue, out string exceptionMessage)
    {
        exceptionMessage = string.Empty;
        try
        {
            knownIssue = KnownIssueHelper.ParseGithubIssue(issue, repositoryWithOwner, KnownIssueType.Infrastructure);
            return true;
        }
        catch (Exception e)
        {
            exceptionMessage = e.Message;
            knownIssue = null;
            return false;
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using BuildInsights.KnownIssues;
using BuildInsights.KnownIssues.Models;

namespace BuildInsights.BuildAnalysis.Models.Views;

public class StepResultView : IResult
{
    public string StepName { get; }
    public string PipelineBuildName { get; }
    public string LinkToBuild { get; }
    public string CreateInfraIssueLink { get; }
    public string CreateRepoIssueLink { get; }
    public string LinkToStep { get; }
    public string LinkToFirstLogError { get; }

    /// <summary>
    /// All the errors that belongs to this step
    /// </summary>
    public IImmutableList<Error> Errors { get; }
    public IImmutableList<KnownIssue> KnownIssues { get; } 
    public FailureRate FailureRate { get; set; }
    public int TotalErrorsCount => Errors.Count;

    /// <summary>
    /// Status of the step in target branch to know if a failure could be related with target
    /// </summary>
    public IImmutableList<string> StepHierarchy { get; }
    public string DisplayStepName { get; }

    public StepResultView(
        StepResult stepResult,
        string pipelineBuildName,
        string linkToBuild,
        MarkdownParameters markdownParameters)
        : this(
            stepName: stepResult.StepName,
            pipelineBuildName: pipelineBuildName,
            linkToBuild: linkToBuild,
            linkToStep: stepResult.LinkToStep,
            errors: stepResult.Errors.ToImmutableList(),
            failureRate: stepResult.FailureRate,
            stepHierarchy: stepResult.StepHierarchy.Select(s => s.Trim()).ToImmutableList(),
            knownIssues: stepResult.KnownIssues,
            markdownParameters)
    {
    }

    public StepResultView(
        string stepName,
        string pipelineBuildName,
        string linkToBuild,
        string linkToStep,
        IImmutableList<Error> errors,
        FailureRate failureRate,
        IImmutableList<string> stepHierarchy,
        IImmutableList<KnownIssue> knownIssues,
        MarkdownParameters parameters)
    {
        StepName = stepName;
        PipelineBuildName = pipelineBuildName;
        LinkToBuild = linkToBuild;
        LinkToStep = linkToStep;
        Errors = errors;
        FailureRate = failureRate;
        StepHierarchy = stepHierarchy;
        KnownIssues = knownIssues;
        LinkToFirstLogError = errors.FirstOrDefault(new Error()).LinkLog;
        DisplayStepName = BuildDisplayStepName();

        KnownIssueUrlOptions urlOptions = parameters.KnownIssueUrlOptions ?? new KnownIssueUrlOptions();
        CreateInfraIssueLink = GetReportIssueUrl(urlOptions.InfrastructureIssueParameters,
            urlOptions.Host, parameters.Repository.Id, parameters.PullRequest);
        CreateRepoIssueLink = GetReportIssueUrl(urlOptions.RepositoryIssueParameters,
            urlOptions.Host, parameters.Repository.Id, parameters.PullRequest);
    }

    private string BuildDisplayStepName()
    {
        // In the event that Azure DevOps doesn't give us a hierarchy of at least three items.
        if (StepHierarchy == null || StepHierarchy.Count < 3)
            return StepName ?? "";


        var localStepHierarchy = StepHierarchy.ToList();

        if (localStepHierarchy[1] == localStepHierarchy[2])
            localStepHierarchy.RemoveAt(2);

        if (localStepHierarchy[0] == "__default")
            localStepHierarchy.RemoveAt(0);

        return string.Join(" / ", localStepHierarchy);
    }

    private string GetReportIssueUrl(IssueParameters issueParameters, string host, string repository, string pullRequest)
    {
        var parameters = new Dictionary<string, string>
        {
            {"build", LinkToStep ?? ""},
            {"build-leg", DisplayStepName},
            {"repository", issueParameters?.Repository ?? repository},
            {"pr", pullRequest ?? "N/A"}
        };

        return KnownIssueHelper.GetReportIssueUrl(parameters, issueParameters, host, repository, pullRequest);
    }
}

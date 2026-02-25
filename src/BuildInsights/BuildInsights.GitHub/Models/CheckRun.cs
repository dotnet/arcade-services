// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Maestro.Common;

namespace BuildInsights.GitHub.Models;

public class CheckRun
{
    private const int AzurePipelinesAppID = 9426;

    public CheckRun(Octokit.CheckRun octoKitCheckRun)
    {
        Status = octoKitCheckRun.Status.Value switch
        {
            Octokit.CheckStatus.Queued => CheckStatus.Queued,
            Octokit.CheckStatus.InProgress => CheckStatus.InProgress,
            Octokit.CheckStatus.Completed => CheckStatus.Completed,
            _ => throw new CheckStatusConversionException($"CheckStatus value: '{octoKitCheckRun.Status.StringValue}' in (Octokit) Check Run '{octoKitCheckRun.Id}' is invalid."),
        };
        AppId = octoKitCheckRun.App.Id;
        if (octoKitCheckRun.Conclusion.HasValue)
        {
            Conclusion = octoKitCheckRun.Conclusion.Value.Value switch
            {
                Octokit.CheckConclusion.Success => CheckConclusion.Success,
                Octokit.CheckConclusion.Failure => CheckConclusion.Failure,
                Octokit.CheckConclusion.Neutral => CheckConclusion.Neutral,
                Octokit.CheckConclusion.Cancelled => CheckConclusion.Cancelled,
                Octokit.CheckConclusion.TimedOut => CheckConclusion.TimedOut,
                Octokit.CheckConclusion.ActionRequired => CheckConclusion.ActionRequired,
                Octokit.CheckConclusion.Skipped => CheckConclusion.Skipped,
                Octokit.CheckConclusion.Stale => CheckConclusion.Stale,
                _ => throw new CheckConclusionConversionException($"CheckConclusion value: '{octoKitCheckRun.Conclusion.Value.StringValue}' in (Octokit) Check Run '{octoKitCheckRun.Id}' is invalid."),
            };
        }
        else
        {
            Conclusion = CheckConclusion.Pending;
        }
        Name = octoKitCheckRun.Name; 
        CheckRunId = octoKitCheckRun.Id;
        Body = octoKitCheckRun.Output?.Text;
        Title = octoKitCheckRun.Output?.Title;
        Summary = octoKitCheckRun.Output?.Summary;

        if (octoKitCheckRun.App.Id == AzurePipelinesAppID && !string.IsNullOrEmpty(octoKitCheckRun.ExternalId))
        {
            (AzureDevOpsPipelineId, AzureDevOpsBuildId, AzureDevOpsProjectId) = ParseExternalId(octoKitCheckRun.ExternalId);
            Organization = BuildUrlUtils.ParseOrganizationFromBuildUrl(octoKitCheckRun.Output?.Summary);
            AzureDevOpsBuildUrl = BuildUrlUtils.GetBuildUrl(Organization, AzureDevOpsProjectId, AzureDevOpsBuildId);
            AzureDevOpsPipelineName = octoKitCheckRun.Name.Split(' ').FirstOrDefault();
        }
        else
        {
            AzureDevOpsPipelineId = 0;
            AzureDevOpsBuildId = 0;
            AzureDevOpsProjectId = null;
        }
    }

    /// <summary>
    /// The CheckRun status
    /// </summary>
    public CheckStatus Status { get; }

    /// <summary>
    /// The Id of the GitHub App the CheckRun belongs to
    /// </summary>
    public long AppId { get; }

    /// <summary>
    /// The Id of the Azure DevOps Pipeline the run was ran on
    /// </summary>
    public int AzureDevOpsPipelineId { get; }

    /// <summary>
    /// The name of the Azure DevOps Pipeline the run was ran on
    /// </summary>
    public string? AzureDevOpsPipelineName { get; }

    /// <summary>
    /// The Id of the Azure DevOps Build
    /// </summary>
    public int AzureDevOpsBuildId { get; }

    /// <summary>
    /// The Id of the Azure DevOps Project the pipeline belongs to
    /// </summary>
    public string? AzureDevOpsProjectId { get; }

    /// <summary>
    /// The Url of the Azure DevOps Build 
    /// </summary>
    public string? AzureDevOpsBuildUrl { get; }

    /// <summary>
    /// The Id of the GitHub CheckRun
    /// </summary>
    public long CheckRunId { get; }

    /// <summary>
    /// Conclusion of the check run
    /// </summary>
    public CheckConclusion Conclusion { get; }

    /// <summary>
    /// Name of the check run
    /// </summary>
    public string Name { get; }


    /// <summary>
    /// Output text of the check run
    /// </summary>
    public string? Body { get; }

    /// <summary>
    /// Summary output text of the check run
    /// </summary>
    public string? Summary { get; }

    /// <summary>
    /// Output text of the check run
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// The organization Id of the Azure DevOps Project the pipeline belongs to
    /// </summary>
    public string? Organization { get; }

    private static (int pipelineId, int buildId, string projectId) ParseExternalId(string externalId)
    {
        string[] parsedExternalId = externalId.Split("|");

        if (parsedExternalId.Length != 3)
        {
            throw new ExternalIdParseException($"External Id '{externalId}' was not in an expected format.");
        }

        try
        {
            string projectId = parsedExternalId[2];
            if (!Guid.TryParse(projectId, out _))
            {
                throw new ExternalIdParseException($"External id has '{projectId}' for project id, expected a GUID");
            }

            return (Convert.ToInt32(parsedExternalId[0]), Convert.ToInt32(parsedExternalId[1]), projectId);
        }
        catch (Exception e)
        {
            throw new ExternalIdParseException(e.Message);
        }
    }
}

public enum CheckStatus
{
    Queued,
    InProgress,
    Completed
}

public enum CheckConclusion
{
    Success,
    Failure,
    Neutral,
    Cancelled,
    TimedOut,
    ActionRequired,
    Skipped,
    Stale,

    // Pending does not exist in Octokit.CheckConclusion
    Pending
}

public class ExternalIdParseException : Exception
{
    public ExternalIdParseException() : base() { }
    public ExternalIdParseException(string message) : base(message) { }
}

public class CheckStatusConversionException : Exception
{
    public CheckStatusConversionException() : base() { }
    public CheckStatusConversionException(string message) : base(message) { }
}

public class CheckConclusionConversionException : Exception
{
    public CheckConclusionConversionException() : base() { }
    public CheckConclusionConversionException(string message) : base(message) { }
}

/// <summary>
/// We consider CheckRuns to be the same if they have the same AppId and the Azure DevOps values are the same.
/// Names may differ, but reference the same Azure DevOps build, and thus, are not included in the comparator
/// </summary>
public class CheckRunEqualityComparer : IEqualityComparer<CheckRun>
{
    public bool Equals([AllowNull] CheckRun x, [AllowNull] CheckRun y)
    {
        if (x == null && y == null)
        {
            return true;
        }
        else if (x == null || y == null)
        {
            return false;
        }
        else if (x.AppId == y.AppId &&
            x.AzureDevOpsBuildId == y.AzureDevOpsBuildId &&
            x.AzureDevOpsPipelineId == y.AzureDevOpsPipelineId &&
            string.Equals(x.AzureDevOpsProjectId,y.AzureDevOpsProjectId))
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public int GetHashCode([DisallowNull] CheckRun obj)
    {
        HashCode hash = new HashCode();
        hash.Add(obj.AppId);
        hash.Add(obj.AzureDevOpsBuildId);
        hash.Add(obj.AzureDevOpsPipelineId);
        hash.Add(obj.AzureDevOpsProjectId);
        return hash.ToHashCode();
    }
}

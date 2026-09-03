// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Maestro.Common;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;

namespace Maestro.MergePolicies;

/// <summary>
///     Merge the PR when it has more than one check and they are all successful, ignoring checks specified in the
///     "ignoreChecks" property.
/// </summary>
public class AllChecksSuccessfulMergePolicy : MergePolicy
{
    private static readonly HashSet<string> s_githubIgnoreChecks =
    [
        "WIP",
        "license/cla",
        "auto-merge.config.enforce",
        "Build Analysis",
    ];

    private static readonly HashSet<string> s_azureDevOpsIgnoreChecks =
    [
        "Comment requirements",
        "Minimum number of reviewers",
        "auto-merge.config.enforce",
        "Work item linking",
    ];

    private readonly HashSet<string> _ignoreChecks;

    public const string WaitingForChecksMsg = "Waiting for checks.";

    public AllChecksSuccessfulMergePolicy(HashSet<string> ignoreChecks)
    {
        _ignoreChecks = ignoreChecks;
    }

    public override string DisplayName => "All Checks Successful";

    public override string Name => "AllChecksSuccessful";

    public override async Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc)
    {
        IEnumerable<Check> checks = await darc.GetPullRequestChecksAsync(pr.Url);
        HashSet<string> defaultIgnoreChecks = GetDefaultIgnoreChecks(pr.Url);
        IEnumerable<Check> notIgnoredChecks = checks.Where(c =>
            !_ignoreChecks.Contains(c.Name)
            && !defaultIgnoreChecks.Contains(c.Name)
            && !c.IsMaestroMergePolicy);

        if (!notIgnoredChecks.Any())
        {
            return Pending(WaitingForChecksMsg);
        }

        // Group check statuses to success, pending and error
        ILookup<CheckState, Check> statuses = notIgnoredChecks.ToLookup(c =>
            c.Status switch
            {
                CheckState.Success or CheckState.Pending => c.Status,
                _ => CheckState.Error,
            });

        int ListChecksCount(CheckState state)
        {
            return statuses[state].Count();
        }

        if (statuses.Contains(CheckState.Error))
        {
            var listChecks = new StringBuilder();
            foreach(var status in statuses[CheckState.Error])
            {
                listChecks.AppendLine($"[{status.Name}]({status.Url})");
            }
            return FailTransiently($"{ListChecksCount(CheckState.Error)} unsuccessful check(s)", listChecks.ToString());
        }

        if (statuses.Contains(CheckState.Pending))
        {
            return Pending($"{ListChecksCount(CheckState.Pending)} pending check(s)");
        }

        return SucceedTransiently($"{ListChecksCount(CheckState.Success)} successful check(s)");
    }

    private static HashSet<string> GetDefaultIgnoreChecks(string pullRequestUrl)
    {
        return GitRepoUrlUtils.ParseTypeFromUri(pullRequestUrl) switch
        {
            GitRepoType.GitHub => s_githubIgnoreChecks,
            GitRepoType.AzureDevOps => s_azureDevOpsIgnoreChecks,
            var repositoryType => throw new NotSupportedException(
                $"Repository type '{repositoryType}' for pull request URL '{pullRequestUrl}' is not supported."),
        };
    }
}

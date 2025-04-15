// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    private readonly HashSet<string> _ignoreChecks;

    public AllChecksSuccessfulMergePolicy(HashSet<string> ignoreChecks)
    {
        _ignoreChecks = ignoreChecks;
    }

    public override string DisplayName => "All Checks Successful";

    public override async Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc)
    {
        IEnumerable<Check> checks = await darc.GetPullRequestChecksAsync(pr.Url);
        IEnumerable<Check> notIgnoredChecks = checks.Where(c => !_ignoreChecks.Contains(c.Name) && !c.IsMaestroMergePolicy);

        if (!notIgnoredChecks.Any())
        {
            return Pending("Waiting for checks.");
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
            return FailTransient($"{ListChecksCount(CheckState.Error)} unsuccessful check(s)", listChecks.ToString());
        }

        if (statuses.Contains(CheckState.Pending))
        {
            return Pending($"{ListChecksCount(CheckState.Pending)} pending check(s)");
        }

        return Succeed($"{ListChecksCount(CheckState.Success)} successful check(s)");
    }
}

public class AllChecksSuccessfulMergePolicyBuilder : IMergePolicyBuilder
{
    public string Name => MergePolicyConstants.AllCheckSuccessfulMergePolicyName;

    public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
    {
        var ignoreChecks = new HashSet<string>(properties.Get<string[]>("ignoreChecks") ?? []);
        return Task.FromResult<IReadOnlyList<IMergePolicy>>([new AllChecksSuccessfulMergePolicy(ignoreChecks)]);
    }
}

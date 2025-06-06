// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Maestro.MergePolicies;

public class NoRequestedChangesMergePolicy : MergePolicy
{
    public override string DisplayName => "No Requested Changes";

    public override string Name => "NoRequestedChanges";

    public override async Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc)
    {
        IEnumerable<Review> reviews = await darc.GetPullRequestReviewsAsync(pr.Url);

        if (reviews.Any(r => r.Status == ReviewState.ChangesRequested || r.Status == ReviewState.Rejected))
        {
            return FailTransiently("There are reviews that have requested changes.");
        }
        else
        {
            return SucceedTransiently("No reviews have requested changes.");
        }
    }
}

public class NoRequestedChangesMergePolicyBuilder : IMergePolicyBuilder
{
    public string Name => MergePolicyConstants.NoRequestedChangesMergePolicyName;

    public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
    {
        IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new NoRequestedChangesMergePolicy() };
        return Task.FromResult(policies);
    }
}

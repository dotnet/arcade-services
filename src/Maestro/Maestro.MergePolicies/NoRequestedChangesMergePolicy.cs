// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Contracts;
using Microsoft.DotNet.DarcLib;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Maestro.MergePolicies
{
    public class NoRequestedChangesMergePolicy : MergePolicy
    {
        public override string DisplayName => "No Requested Changes";

        public override async Task<MergePolicyEvaluationResult> EvaluateAsync(IPullRequest pr, IRemote darc)
        {
            IEnumerable<Review> reviews = await darc.GetPullRequestReviewsAsync(pr.Url);

            if (reviews.Any(r => r.Status == ReviewState.ChangesRequested || r.Status == ReviewState.Rejected))
            {
                return await Fail("There are reviews that have requested changes.");
            }
            else
            {
                return await Succeed("No reviews have requested changes.");
            }
        }
    }

    public class NoRequestedChangesMergePolicyBuilder : IMergePolicyBuilder
    {
        public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, IPullRequest pr)
        {
            IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new NoRequestedChangesMergePolicy() };
            return Task.FromResult(policies);
        }
    }
}

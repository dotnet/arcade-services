// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Maestro.MergePolicies
{
    public class NoRequestedChangesMergePolicy : MergePolicy
    {
        public override string DisplayName => "No Requested Changes";

        public static async Task EvaluateReviewAsync(IMergePolicyEvaluationContext context)
        {
            IEnumerable<Review> reviews = await context.Darc.GetPullRequestReviewsAsync(context.PullRequestUrl);

            if (reviews.Any(r => r.Status == ReviewState.ChangesRequested || r.Status == ReviewState.Rejected))
            {
                context.Fail("There are reviews that have requested changes.");
            }
            else
            {
                context.Succeed("No reviews have requested changes.");
            }
        }

        public override async Task EvaluateAsync(IMergePolicyEvaluationContext context, MergePolicyProperties properties)
        {
            await EvaluateReviewAsync(context);
        }
    }
}

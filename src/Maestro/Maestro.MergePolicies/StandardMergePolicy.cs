// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Maestro.MergePolicies
{
    public class StandardMergePolicy : MergePolicy
    {
        public StandardMergePolicy()
        {
            standardGithubIgnoredChecks = new HashSet<string>
            {
                "WIP",
                "license/cla"
            };
            standardAzureDevOpsIgnoredChecks = new HashSet<string>
            {
                "Comment requirements",
                "Minimum number of reviewers",
                "Required reviewers",
                "Work item linking"
            };
        }

        public override string DisplayName => "Standard Github Merge Policies";

        public readonly HashSet<string> standardGithubIgnoredChecks;
        public readonly HashSet<string> standardAzureDevOpsIgnoredChecks;

        public override async Task EvaluateAsync(IMergePolicyEvaluationContext context, MergePolicyProperties properties)
        {
            var ignoredChecks = context.PullRequestUrl.Contains("github.com") ?
                standardGithubIgnoredChecks : standardAzureDevOpsIgnoredChecks;
            await AllChecksSuccessfulMergePolicy.EvaluateChecksAsync(context, ignoredChecks);
            await NoRequestedChangesMergePolicy.EvaluateReviewAsync(context);
        }
    }
}

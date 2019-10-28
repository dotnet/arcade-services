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
                "license/cla",
                "auto-merge.config.enforce"
            };
            standardAzureDevOpsIgnoredChecks = new HashSet<string>
            {
                "Comment requirements",
                "Minimum number of reviewers",
                "Required reviewers",
                "Work item linking"
            };
        }

        public override string DisplayName => "Standard Merge Policies";

        public readonly HashSet<string> standardGithubIgnoredChecks;
        public readonly HashSet<string> standardAzureDevOpsIgnoredChecks;

        public override async Task EvaluateAsync(IMergePolicyEvaluationContext context, MergePolicyProperties properties)
        {
            HashSet<string> ignoredChecks = null;
            string prUrl = context.PullRequestUrl;
            if (prUrl.Contains("github.com"))
            {
                ignoredChecks = standardGithubIgnoredChecks;
            }
            else if (prUrl.Contains("dev.azure.com"))
            {
                ignoredChecks = standardAzureDevOpsIgnoredChecks;
            }
            else
            {
                throw new NotImplementedException("Unknown pr repo url");
            }
            await AllChecksSuccessfulMergePolicy.EvaluateChecksAsync(context, ignoredChecks);
            await NoRequestedChangesMergePolicy.EvaluateReviewAsync(context);
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;

namespace Maestro.MergePolicies
{
    /// <summary>
    ///     Merge the PR when it has more than one check and they are all successful, ignoring checks specified in the
    ///     "ignoreChecks" property.
    /// </summary>
    public class AllChecksSuccessfulMergePolicy : MergePolicy
    {
        public override string DisplayName => "All Checks Successful";

        public static async Task EvaluateChecks(IMergePolicyEvaluationContext context, HashSet<string> ignoredChecks)
        {
            IEnumerable<Check> checks = await context.Darc.GetPullRequestChecksAsync(context.PullRequestUrl);
            IEnumerable<Check> notIgnoredChecks = checks.Where(c => !ignoredChecks.Contains(c.Name));

            if (!notIgnoredChecks.Any())
            {
                context.Fail("No un-ignored checks.");
                return;
            }

            ILookup<CheckState, Check> statuses = notIgnoredChecks.ToLookup(
                c =>
                {
                    // unify the check statuses to success, pending, and error
                    switch (c.Status)
                    {
                        case CheckState.Success:
                        case CheckState.Pending:
                            return c.Status;
                        default:
                            return CheckState.Error;
                    }
                });

            string ListChecks(CheckState state)
            {
                return string.Join(", ", statuses[state].Select(c => c.Name));
            }

            if (statuses.Contains(CheckState.Error))
            {
                context.Fail($"Unsuccessful checks: {ListChecks(CheckState.Error)}");
                return;
            }

            if (statuses.Contains(CheckState.Pending))
            {
                context.Pending($"Waiting on checks: {ListChecks(CheckState.Pending)}");
                return;
            }

            context.Succeed($"Successful checks: {ListChecks(CheckState.Success)}");
        }

        public override async Task EvaluateAsync(
            IMergePolicyEvaluationContext context,
            MergePolicyProperties properties)
        {
            var ignoreChecks = new HashSet<string>(properties.Get<string[]>("ignoreChecks") ?? Array.Empty<string>());

            await EvaluateChecks(context, ignoreChecks);
        }
    }
}

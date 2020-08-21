// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Contracts;
using Microsoft.DotNet.DarcLib;

namespace Maestro.MergePolicies
{
    /// <summary>
    ///     Merge the PR when it has more than one check and they are all successful, ignoring checks specified in the
    ///     "ignoreChecks" property.
    /// </summary>
    public class AllChecksSuccessfulMergePolicy : MergePolicy
    {
        private HashSet<string> _ignoreChecks;

        public AllChecksSuccessfulMergePolicy(HashSet<string> ignoreChecks)
        {
            _ignoreChecks = ignoreChecks;
        }

        public override string DisplayName => "All Checks Successful";

        public override async Task<MergePolicyEvaluationResult> EvaluateAsync(IPullRequest pr, IRemote darc)
        {
            IEnumerable<Check> checks = await darc.GetPullRequestChecksAsync(pr.Url);
            IEnumerable<Check> notIgnoredChecks = checks.Where(c => !_ignoreChecks.Contains(c.Name) && !c.IsMaestroMergePolicy);

            if (!notIgnoredChecks.Any())
            {
                return Fail("Waiting for checks.");
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
                return Fail($"Unsuccessful checks: {ListChecks(CheckState.Error).Count()}");
            }

            if (statuses.Contains(CheckState.Pending))
            {
                return Pending($"Waiting on checks: {ListChecks(CheckState.Pending).Count()}");
            }

            return Succeed($"Successful checks: {ListChecks(CheckState.Success).Count()}");
        }
    }

    public class AllChecksSuccessfulMergePolicyBuilder : IMergePolicyBuilder
    {
        public string Name => MergePolicyConstants.AllCheckSuccessfulMergePolicyName;

        public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, IPullRequest pr)
        {
            var ignoreChecks = new HashSet<string>(properties.Get<string[]>("ignoreChecks") ?? Array.Empty<string>());
            IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new AllChecksSuccessfulMergePolicy(ignoreChecks) };
            return Task.FromResult(policies);
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Contracts;
using Microsoft.DotNet.DarcLib;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Maestro.MergePolicies
{
    /// <summary>
    ///     Merge the PR when it only has commits created by Maestro. This is not yet implemented.
    /// </summary>
    public class NoExtraCommitsMergePolicy : MergePolicy
    {
        public override string DisplayName => "No Extra Commits";

        public override Task<MergePolicyEvaluationResult> EvaluateAsync(IPullRequest pr, IRemote darc)
        {
            return Task.FromResult(
                Fail("Merge Policy Not Yet Implemented."));
        }
    }

    public class NoExtraCommitsMergePolicyBuilder : IMergePolicyBuilder
    {
        public string Name => MergePolicyConstants.NoExtraCommitsMergePolicyName;

        public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, IPullRequest pr)
        {
            IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new NoExtraCommitsMergePolicy() };
            return Task.FromResult(policies);
        }
    }
}

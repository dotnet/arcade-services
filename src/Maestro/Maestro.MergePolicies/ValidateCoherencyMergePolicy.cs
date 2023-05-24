// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Contracts;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Maestro.MergePolicies;

public class ValidateCoherencyMergePolicy : MergePolicy
{
    public override string DisplayName => "Validate coherency";

    public override Task<MergePolicyEvaluationResult> EvaluateAsync(IPullRequest pr, IRemote darc) =>
        Task.FromResult(pr.CoherencyCheckSuccessful ?
            Succeed("Coherency check successful.") :
            Fail("Coherency check failed.",
                string.Concat("Coherency update failed for the following dependencies:",
                    string.Concat(pr.CoherencyErrors.Select(error =>
                        string.Concat("\n * ", error.Error, error.PotentialSolutions.Count() < 1 ? "" :
                            "\n    PotentialSolutions:" + string.Concat(error.PotentialSolutions.Select(s => "\n     * " + s))))),
                    "\n\nThe documentation can be found at this location: ",
                    "https://github.com/dotnet/arcade/blob/main/Documentation/Darc.md#coherent-parent-dependencies")));
}

public class ValidateCoherencyMergePolicyBuilder : IMergePolicyBuilder
{
    public string Name => MergePolicyConstants.ValidateCoherencyMergePolicyName;

    public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, IPullRequest pr)
    {
        IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new ValidateCoherencyMergePolicy() };
        return Task.FromResult(policies);
    }
}

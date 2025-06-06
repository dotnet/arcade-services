// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Maestro.MergePolicies;

public class ValidateCoherencyMergePolicy : MergePolicy
{
    public override string DisplayName => "Validate coherency";

    public override string Name => "ValidateCoherency";

    public override Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc)
    {
        if (pr.CoherencyCheckSuccessful.GetValueOrDefault(true))
            return Task.FromResult(SucceedDecisively("Coherency check successful."));

        var description = new StringBuilder("Coherency update failed for the following dependencies:");
        foreach (CoherencyErrorDetails error in pr.CoherencyErrors ?? [])
        {
            description.Append("\n * ").Append(error.Error);

            if (error.PotentialSolutions.Any())
            {
                description.Append("\n    PotentialSolutions:");
                foreach (string solution in error.PotentialSolutions)
                {
                    description.Append("\n     * ").Append(solution);
                }
            }
        }
        description
            .Append("\n\nThe documentation can be found at this location: ")
            .Append("https://github.com/dotnet/arcade/blob/main/Documentation/Darc.md#coherent-parent-dependencies");

        return Task.FromResult(FailDecisively("Coherency check failed.", description.ToString()));
    }
}

public class ValidateCoherencyMergePolicyBuilder : IMergePolicyBuilder
{
    public string Name => MergePolicyConstants.ValidateCoherencyMergePolicyName;

    public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
    {
        IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new ValidateCoherencyMergePolicy() };
        return Task.FromResult(policies);
    }
}

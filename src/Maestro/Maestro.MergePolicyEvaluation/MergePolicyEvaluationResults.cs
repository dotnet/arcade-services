// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;

#nullable enable
namespace Maestro.MergePolicyEvaluation;

public class MergePolicyEvaluationResults
{
    public MergePolicyEvaluationResults(IEnumerable<MergePolicyEvaluationResult> results)
    {
        Results = results.ToImmutableList();
    }

    public IImmutableList<MergePolicyEvaluationResult> Results { get; }

    public bool Succeeded => Results.Count > 0 && Results.All(r => r.Status == MergePolicyEvaluationStatus.Success);

    public bool Pending => Results.Any(r => r.Status == MergePolicyEvaluationStatus.Pending);

    public bool Failed => Results.Any(r => r.Status == MergePolicyEvaluationStatus.Failure);
}

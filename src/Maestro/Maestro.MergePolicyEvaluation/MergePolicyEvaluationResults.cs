// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.MergePolicyEvaluation;

public class MergePolicyEvaluationResults
{

    public MergePolicyEvaluationResults(string id, IReadOnlyCollection<MergePolicyEvaluationResult> results, string targetCommitSha)
    {
        Id = id;
        Results = results;
        TargetCommitSha = targetCommitSha;
    }

    public string Id { get; set; }

    public IReadOnlyCollection<MergePolicyEvaluationResult> Results { get; set; }

    public string TargetCommitSha { get; set; }

    public bool Succeeded => Results.Any() && Results.All(r => r.Status is MergePolicyEvaluationStatus.DecisiveSuccess or MergePolicyEvaluationStatus.TransientSuccess);

    public bool Pending => Results.Any(r => r.Status == MergePolicyEvaluationStatus.Pending);

    public bool Failed => Results.Any(r => r.Status is MergePolicyEvaluationStatus.DecisiveFailure or MergePolicyEvaluationStatus.TransientFailure);
}

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

    public bool Succeeded => Results.Count() > 0 && Results.All(r => r.Status == MergePolicyEvaluationStatus.DecisiveSuccess);

    public bool Pending => Results.Any(r => r.Status == MergePolicyEvaluationStatus.Pending);

    public bool Failed => Results.Any(r => r.Status == MergePolicyEvaluationStatus.DecisiveFailure || r.Status == MergePolicyEvaluationStatus.TransientFailure);
}

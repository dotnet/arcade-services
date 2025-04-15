// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Maestro.MergePolicyEvaluation;

namespace Maestro.MergePolicies;
public record PRCheckEvaluationAtCommit
{
    public PRCheckEvaluationAtCommit(
        Dictionary<string, MergePolicyEvaluationStatus> evaluationResults,
        string targetCommitSha)
    {
        EvaluationResults = evaluationResults;
        TargetCommitSha = targetCommitSha ?? throw new ArgumentNullException(nameof(targetCommitSha));
    }
    public Dictionary<string,MergePolicyEvaluationStatus> EvaluationResults { get; init; }
    public string TargetCommitSha { get; init; }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.MergePolicyEvaluation;

public enum MergePolicyEvaluationStatus
{
    Pending = 0,
    DecisiveSuccess, // success that doesn't need to be evaluated again if the commit didn't change
    TransientSuccess, // success that needs to be re-evaluated even when on the same commit
    DecisiveFailure, // failure that cannot be resolved by simply re-evaluating without adding new commits
    TransientFailure, // failure that may be resolved by simply re-evaluating
}

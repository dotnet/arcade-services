// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.MergePolicyEvaluation;

public enum MergePolicyEvaluationStatus
{
    Pending = 0,
    Success,
    DecisiveFailure, // failures that cannot be resolved by simply re-evaluating
    TransientFailure, // failures that may be resolved by re-evaluating
}

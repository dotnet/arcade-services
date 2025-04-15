// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.MergePolicyEvaluation;

public class MergePolicyEvaluationResult
{
    public MergePolicyEvaluationResult(MergePolicyEvaluationStatus status, string title, string message, string mergePolicyName, string mergePolicyDisplayName)
    {
        if (mergePolicyName == null)
        {
            throw new ArgumentNullException($"{nameof(mergePolicyName)}");
        }
        if (mergePolicyDisplayName == null)
        {
            throw new ArgumentNullException($"{nameof(mergePolicyDisplayName)}");
        }

        Status = status;
        Title = title;
        Message = message;
        MergePolicyName = mergePolicyName;
        MergePolicyDisplayName = mergePolicyDisplayName;
    }

    public MergePolicyEvaluationStatus Status { get; }
    public string Title { get; }
    public string Message { get; }
    public string MergePolicyName { get; }
    public string MergePolicyDisplayName { get; }
}

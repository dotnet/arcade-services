// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.MergePolicyEvaluation;

public class MergePolicyEvaluationResult
{
    public MergePolicyEvaluationResult(MergePolicyEvaluationStatus status, string title, string message, IMergePolicyInfo mergePolicy)
    {
        if (mergePolicy == null)
        {
            throw new ArgumentNullException(nameof(mergePolicy));
        }
        if (mergePolicy.Name == null)
        {
            throw new ArgumentNullException($"{nameof(mergePolicy)}.{nameof(mergePolicy.Name)}");
        }
        if (mergePolicy.DisplayName == null)
        {
            throw new ArgumentNullException($"{nameof(mergePolicy)}.{nameof(mergePolicy.DisplayName)}");
        }

        Status = status;
        Title = title;
        Message = message;
        MergePolicyInfo = mergePolicy;
    }

    public MergePolicyEvaluationStatus Status { get; }
    public string Title { get; }
    public string Message { get; }
    public IMergePolicyInfo MergePolicyInfo { get; }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Maestro.Contracts
{
    public class MergePolicyEvaluationResults
    {
        public MergePolicyEvaluationResults(IEnumerable<MergePolicyEvaluationResult> results)
        {
            Results = results.ToImmutableList();
        }

        public IReadOnlyList<MergePolicyEvaluationResult> Results { get; }

        public bool Succeeded => Results.Count > 0 && Results.All(r => r.Status == MergePolicyEvaluationStatus.Success);

        public bool Pending => Results.Count > 0 && Results.Any(r => r.Status == MergePolicyEvaluationStatus.Pending);

        public bool Failed => Results.Count > 0 && Results.Any(r => r.Status == MergePolicyEvaluationStatus.Failure);
    }

    public class MergePolicyEvaluationResult
    {
        public MergePolicyEvaluationResult(MergePolicyEvaluationStatus status, string message, IMergePolicyInfo mergePolicy)
        {
            if (mergePolicy.Name == null)
            {
                throw new ArgumentNullException(nameof(mergePolicy.Name));
            }
            if (mergePolicy.DisplayName == null)
            {
                throw new ArgumentNullException(nameof(mergePolicy.DisplayName));
            }

            Status = status;
            Message = message;
            MergePolicyInfo = mergePolicy;
        }

        public MergePolicyEvaluationStatus Status { get; }
        public string Message { get; }
        public IMergePolicyInfo MergePolicyInfo { get; }
    }

    public enum MergePolicyEvaluationStatus
    {
        Pending = 0,
        Success,
        Failure,
    }
}

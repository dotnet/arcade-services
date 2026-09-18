// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;

namespace Maestro.MergePolicies;

public abstract class MergePolicy : IMergePolicy
{
    public abstract string Name { get; }

    public abstract string DisplayName { get; }

    public abstract Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc);

    public MergePolicyEvaluationResult Pending(string title) => new(MergePolicyEvaluationStatus.Pending, title, string.Empty, Name, DisplayName);

    public MergePolicyEvaluationResult SucceedDecisively(string title) => new(MergePolicyEvaluationStatus.DecisiveSuccess, title, string.Empty, Name, DisplayName);
    public MergePolicyEvaluationResult SucceedTransiently(string title) => new(MergePolicyEvaluationStatus.TransientSuccess, title, string.Empty, Name, DisplayName);

    public MergePolicyEvaluationResult FailDecisively(string title) => new(MergePolicyEvaluationStatus.DecisiveFailure, title, string.Empty, Name, DisplayName);

    public MergePolicyEvaluationResult FailTransiently(string title) => new(MergePolicyEvaluationStatus.TransientFailure, title, string.Empty, Name, DisplayName);

    public MergePolicyEvaluationResult FailDecisively(string title, string message) => new(MergePolicyEvaluationStatus.DecisiveFailure, title, message, Name, DisplayName);

    public MergePolicyEvaluationResult FailTransiently(string title, string message) => new(MergePolicyEvaluationStatus.TransientFailure, title, message, Name, DisplayName);
}

public interface IMergePolicy : IMergePolicyInfo
{
    Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc);
}

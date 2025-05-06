// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Newtonsoft.Json.Linq;

namespace Maestro.MergePolicies;

public class MergePolicyProperties
{
    public MergePolicyProperties(IReadOnlyDictionary<string, JToken> properties)
    {
        Properties = properties;
    }

    public IReadOnlyDictionary<string, JToken> Properties { get; }

    public T Get<T>(string key)
    {
        T result = default;
        if (Properties != null && Properties.TryGetValue(key, out JToken value))
        {
            result = value.ToObject<T>();
        }

        return result;
    }
}

public abstract class MergePolicy : IMergePolicy
{
    public abstract string Name { get; }

    public abstract string DisplayName { get; }

    public abstract Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc);

    public MergePolicyEvaluationResult Pending(string title) => new(MergePolicyEvaluationStatus.Pending, title, string.Empty, this.Name, this.DisplayName);

    public MergePolicyEvaluationResult SucceedDecisively(string title) => new(MergePolicyEvaluationStatus.DecisiveSuccess, title, string.Empty, this.Name, this.DisplayName);
    public MergePolicyEvaluationResult SucceedTransiently(string title) => new(MergePolicyEvaluationStatus.TransientSuccess, title, string.Empty, this.Name, this.DisplayName);

    public MergePolicyEvaluationResult FailDecisively(string title) => new(MergePolicyEvaluationStatus.DecisiveFailure, title, string.Empty, this.Name, this.DisplayName);

    public MergePolicyEvaluationResult FailTransiently(string title) => new(MergePolicyEvaluationStatus.TransientFailure, title, string.Empty, this.Name, this.DisplayName);

    public MergePolicyEvaluationResult FailDecisively(string title, string message) => new(MergePolicyEvaluationStatus.DecisiveFailure, title, message, this.Name, this.DisplayName);

    public MergePolicyEvaluationResult FailTransiently(string title, string message) => new(MergePolicyEvaluationStatus.TransientFailure, title, message, this.Name, this.DisplayName);
}

public interface IMergePolicy : IMergePolicyInfo
{
    Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc);
}

public interface IMergePolicyBuilder
{
    string Name { get; }

    /// <summary>
    /// Creates list of instances of concrete merge policies which shall be evaluated 
    /// for particular merge policy definition
    /// In most cases it will return array of exactly one merge policy, but in special cases like standard-policy
    /// it will return multiple pre-configured policies which that policies template consist of
    /// </summary>
    Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr);
}

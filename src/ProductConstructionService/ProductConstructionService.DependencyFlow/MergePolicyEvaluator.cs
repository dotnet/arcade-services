// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Maestro.Data.Models;
using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.DependencyFlow;

internal interface IMergePolicyEvaluator
{
    Task<IEnumerable<MergePolicyEvaluationResult>> EvaluateAsync(
        PullRequestUpdateSummary pr,
        IRemote darc,
        IReadOnlyList<MergePolicyDefinition> policyDefinitions,
        MergePolicyEvaluationResults? cachedResults,
        string targetBranchSha);
}

internal class MergePolicyEvaluator : IMergePolicyEvaluator
{
    private readonly OperationManager _operations;

    public MergePolicyEvaluator(
        IEnumerable<IMergePolicyBuilder> mergePolicies,
        OperationManager operations,
        ILogger<MergePolicyEvaluator> logger)
    {
        MergePolicyBuilders = mergePolicies.ToImmutableDictionary(p => p.Name);
        Logger = logger;
        _operations = operations;
    }

    public IImmutableDictionary<string, IMergePolicyBuilder> MergePolicyBuilders { get; }
    public ILogger<MergePolicyEvaluator> Logger { get; }

    public async Task<IEnumerable<MergePolicyEvaluationResult>> EvaluateAsync(
        PullRequestUpdateSummary pr,
        IRemote darc,
        IReadOnlyList<MergePolicyDefinition> policyDefinitions,
        MergePolicyEvaluationResults? cachedResults,
        string targetBranchSha)
    {
        Dictionary<string, MergePolicyEvaluationResult> resultsByPolicyName = new();
        IDictionary<string, MergePolicyEvaluationResult> cachedResultsByPolicyName =
            cachedResults?.Results.ToDictionary(r => r.MergePolicyName, r => r) ?? new Dictionary<string, MergePolicyEvaluationResult>();

        foreach (MergePolicyDefinition definition in policyDefinitions)
        {
            if (MergePolicyBuilders.TryGetValue(definition.Name, out IMergePolicyBuilder? policyBuilder))
            {
                using var oDef = _operations.BeginOperation("Evaluating Merge Definition {policyName}", definition.Name);
                var policies = await policyBuilder.BuildMergePoliciesAsync(new MergePolicyProperties(definition.Properties), pr);
                foreach (var policy in policies)
                {
                    if (cachedResultsByPolicyName.TryGetValue(policy.Name, out var cachedEvaluationResult) &&
                        CanSkipRerunningPRCheck(cachedResults?.TargetCommitSha, cachedEvaluationResult, targetBranchSha))
                    {
                        Logger.LogInformation("Skipping re-evaluation of {policyName}, which has result {policyResult} at commitSha {commitSha}",
                            policy.Name, cachedEvaluationResult.Status, cachedResults?.TargetCommitSha);
                        cachedEvaluationResult.IsCachedResult = true;
                        resultsByPolicyName[policy.Name] = cachedEvaluationResult;
                    }
                    else
                    {
                        using var oPol = _operations.BeginOperation("Evaluating Merge Policy {policyName}", policy.Name);
                        resultsByPolicyName[policy.Name] = await policy.EvaluateAsync(pr, darc);
                    }
                }
            }
            else
            {
                var notImplemented = new NotImplementedMergePolicy(definition.Name);
                resultsByPolicyName[definition.Name] = new MergePolicyEvaluationResult(
                    MergePolicyEvaluationStatus.DecisiveFailure,
                    $"Unknown Merge Policy: '{definition.Name}'",
                    string.Empty,
                    string.Empty,
                    notImplemented.DisplayName);
            }
        }
        return resultsByPolicyName.Values;
    }

    private static bool CanSkipRerunningPRCheck(
        string? cachedCommitSha,
        MergePolicyEvaluationResult? cachedEvaluationValue,
        string targetBranchSha)
    {
        if (cachedCommitSha == null || !cachedCommitSha.Equals(targetBranchSha))
        {
            return false;
        }
        return cachedEvaluationValue?.Status is MergePolicyEvaluationStatus.DecisiveFailure or MergePolicyEvaluationStatus.DecisiveSuccess;
    }

    private class NotImplementedMergePolicy : MergePolicy
    {
        private readonly string _definitionName;

        public override string Name => "NotImplemented";

        public NotImplementedMergePolicy(string definitionName)
        {
            _definitionName = definitionName;
        }

        public override string DisplayName => $"Not implemented merge policy '{_definitionName}'";

        public override Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc)
        {
            throw new NotImplementedException();
        }
    }
}

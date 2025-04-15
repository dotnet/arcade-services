// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Maestro.Data.Models;
using Maestro.MergePolicies;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;

namespace ProductConstructionService.DependencyFlow;

internal interface IMergePolicyEvaluator
{
    Task<MergePolicyEvaluationResults> EvaluateAsync(
        PullRequestUpdateSummary pr,
        IRemote darc,
        IReadOnlyList<MergePolicyDefinition> policyDefinitions);
}

internal class MergePolicyEvaluator : IMergePolicyEvaluator
{
    private readonly OperationManager _operations;

    protected readonly IRedisCache<PRCheckEvaluationAtCommit> _prCheckResultState;

    public MergePolicyEvaluator(
        PullRequestUpdaterId id,
        IEnumerable<IMergePolicyBuilder> mergePolicies,
        OperationManager operations,
        ILogger<MergePolicyEvaluator> logger,
        IRedisCacheFactory cacheFactory)
    {
        MergePolicyBuilders = mergePolicies.ToImmutableDictionary(p => p.Name);
        Logger = logger;
        _operations = operations;
        _prCheckResultState = cacheFactory.Create<PRCheckEvaluationAtCommit>(id.ToString());
    }

    public IImmutableDictionary<string, IMergePolicyBuilder> MergePolicyBuilders { get; }
    public ILogger<MergePolicyEvaluator> Logger { get; }

    public async Task<MergePolicyEvaluationResults> EvaluateAsync(
        PullRequestUpdateSummary pr,
        IRemote darc,
        IReadOnlyList<MergePolicyDefinition> policyDefinitions)
    {
        var results = new List<MergePolicyEvaluationResult>();
        foreach (MergePolicyDefinition definition in policyDefinitions)
        {
            PRCheckEvaluationAtCommit? existingPRCheckEvaluationResult = await _prCheckResultState.TryGetStateAsync();
            if (CanSkipRerunningPRCheck(existingPRCheckEvaluationResult, pr, definition.Name))
            {
                continue;
            }
            if (MergePolicyBuilders.TryGetValue(definition.Name, out IMergePolicyBuilder? policyBuilder))
            {
                using var oDef = _operations.BeginOperation("Evaluating Merge Definition {policyName}", definition.Name);
                var policies = await policyBuilder.BuildMergePoliciesAsync(new MergePolicyProperties(definition.Properties), pr);
                foreach (var policy in policies)
                {
                    using var oPol = _operations.BeginOperation("Evaluating Merge Policy {policyName}", policy.Name);
                    results.Add(await policy.EvaluateAsync(pr, darc));
                }
            }
            else
            {
                var notImplemented = new NotImplementedMergePolicy(definition.Name);
                results.Add(new MergePolicyEvaluationResult(MergePolicyEvaluationStatus.PermanentFailure, $"Unknown Merge Policy: '{definition.Name}'", string.Empty, notImplemented));
            }
        }
        var newPRCheckEvaluationResult = new PRCheckEvaluationAtCommit(
            results.ToDictionary(result => result.MergePolicyInfo.Name, result => result.Status),
            pr.TargetSha);
        await _prCheckResultState.SetAsync(newPRCheckEvaluationResult);
        return new MergePolicyEvaluationResults(results);
    }

    private static bool CanSkipRerunningPRCheck(    
        PRCheckEvaluationAtCommit? existingPRCheckEvaluationResult,
        PullRequestUpdateSummary pr,
        string policyName)
    {
        if (existingPRCheckEvaluationResult == null || pr.TargetSha.Equals(existingPRCheckEvaluationResult.TargetCommitSha))
        {
            return false;
        }
        var status = existingPRCheckEvaluationResult.EvaluationResults.GetValueOrDefault(policyName);
        return status is MergePolicyEvaluationStatus.PermanentFailure or MergePolicyEvaluationStatus.Success;
    }

    private class NotImplementedMergePolicy : MergePolicy
    {
        private readonly string _definitionName;

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

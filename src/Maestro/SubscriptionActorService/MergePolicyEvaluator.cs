// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Maestro.Contracts;
using Maestro.Data.Models;
using Maestro.MergePolicies;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.Extensions.Logging;

namespace SubscriptionActorService
{
    public class MergePolicyEvaluator : IMergePolicyEvaluator
    {
        private readonly OperationManager _operations;

        public MergePolicyEvaluator(IEnumerable<IMergePolicyBuilder> mergePolicies, OperationManager operations, ILogger<MergePolicyEvaluator> logger)
        {
            MergePolicyBuilders = mergePolicies.ToImmutableDictionary(p => p.Name);
            Logger = logger;
            _operations = operations;
        }

        public IImmutableDictionary<string, IMergePolicyBuilder> MergePolicyBuilders { get; }
        public ILogger<MergePolicyEvaluator> Logger { get; }

        public async Task<MergePolicyEvaluationResults> EvaluateAsync(
            IPullRequest pr,
            IRemote darc,
            IReadOnlyList<MergePolicyDefinition> policyDefinitions)
        {
            var results = new List<MergePolicyEvaluationResult>();
            foreach (MergePolicyDefinition definition in policyDefinitions)
            {
                if (MergePolicyBuilders.TryGetValue(definition.Name, out IMergePolicyBuilder policyBuilder))
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
                    results.Add(new MergePolicyEvaluationResult(MergePolicyEvaluationStatus.Failure, $"Unknown Merge Policy: '{definition.Name}'", notImplemented));
                }
            }

            return new MergePolicyEvaluationResults(results);
        }

        private class NotImplementedMergePolicy : MergePolicy
        {
            private string _definitionName;

            public NotImplementedMergePolicy(string definitionName)
            {
                _definitionName = definitionName;
            }

            public override string DisplayName => $"Not implemented merge policy '{_definitionName}'";

            public override Task<MergePolicyEvaluationResult> EvaluateAsync(IPullRequest pr, IRemote darc)
            {
                throw new System.NotImplementedException();
            }
        }
    }
}

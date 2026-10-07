// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Maestro.MergePolicies;

public interface IMergePolicyBuilder
{
    IReadOnlyList<IMergePolicy> BuildBatchedSubscriptionMergePolicies(RepositoryBranch? repositoryBranch);
    IReadOnlyList<IMergePolicy> BuildNonBatchedSubscriptionMergePolicies(Subscription subscription);
}

public class MergePolicyBuilder(IBasicBarClient barClient, ILogger<IMergePolicy> logger) : IMergePolicyBuilder
{

    private readonly IBasicBarClient _barClient = barClient;
    private readonly ILogger<IMergePolicy> _logger = logger;

    private static List<IMergePolicy> BuildCommonMergePolicies(IEnumerable<string> ignoredChecks) =>
        [
            new VersionDetailsPropsMergePolicy(),
            new DontAutomergeDowngradesMergePolicy(),
            new ValidateCoherencyMergePolicy(),
            new AllChecksSuccessfulMergePolicy([.. ignoredChecks])
        ];

    public IReadOnlyList<IMergePolicy> BuildBatchedSubscriptionMergePolicies(RepositoryBranch? repositoryBranch)
        => BuildCommonMergePolicies(repositoryBranch?.IgnoredChecks ?? []);

    public IReadOnlyList<IMergePolicy> BuildNonBatchedSubscriptionMergePolicies(Subscription subscription)
    {
        var policies = BuildCommonMergePolicies(subscription.IgnoredChecks);

        if (subscription.SourceEnabled)
        {
            if (subscription.IsForwardFlow())
            {
                policies.Add(new ForwardFlowMergePolicy(_barClient, _logger));
            }
            else
            {
                policies.Add(new BackFlowMergePolicy(_barClient, _logger));
            }
        }

        return policies;
    }
}

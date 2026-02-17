// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.WorkItems.Models;
using BuildInsights.GitHub;
using ProductConstructionService.WorkItems;

namespace BuildInsights.BuildAnalysis.WorkItems.Processors;

public class KnownIssueValidationProcessor : WorkItemProcessor<KnownIssueValidationRequest>
{
    private readonly IGitHubChecksService _gitHubChecksService;
    private readonly IKnownIssueValidationService _knownIssueValidationService;

    public KnownIssueValidationProcessor(
        IGitHubChecksService gitHubChecksService,
        IKnownIssueValidationService knownIssueValidationService)
    {
        _gitHubChecksService = gitHubChecksService;
        _knownIssueValidationService = knownIssueValidationService;
    }

    public override async Task<bool> ProcessWorkItemAsync(
        KnownIssueValidationRequest workItem,
        CancellationToken cancellationToken)
    {
        if (await _gitHubChecksService.IsRepositorySupported(workItem.RepositoryWithOwner))
        {
            await _knownIssueValidationService.ValidateKnownIssue(workItem, cancellationToken);
        }

        return true;
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue.Jobs;

/// <summary>
/// Main code flow job which causes new code changes to be flown to a new branch in the target repo.
/// </summary>
internal class CodeFlowJob : Job
{
    /// <summary>
    /// Subscription that is being flown/triggered.
    /// </summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>
    /// Build that is being flown.
    /// </summary>
    public required int BuildId { get; init; }

    /// <summary>
    /// Name of the PR branch that will be created in the target repo.
    /// </summary>
    public required string PrBranch { get; init; }

    /// <summary>
    /// URL to the code flow PR.
    /// </summary>
    public string? PrUrl { get; init; }

    public override string Type => nameof(CodeFlowJob);
}

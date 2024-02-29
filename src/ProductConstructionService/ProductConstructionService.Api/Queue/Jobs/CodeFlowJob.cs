// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue.Jobs;

/// <summary>
/// Main code flow job which causes new code changes to be flown to a new branch in the target repo.
/// </summary>
internal class CodeFlowJob : Job
{
    public required Guid SubscriptionId { get; init; }
    public required int BuildId { get; init; }
    public required string TargetBranch { get; init; }

    public override string Type => nameof(CodeFlowJob);
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue.Jobs;

public class CodeFlowJob : Job
{
    public required string SubscriptionId { get; init; }
    public required int BuildId { get; init; }
    public override string Type => nameof(CodeFlowJob);
}

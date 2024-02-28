// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Controllers.Models;

internal class CreateBranchRequest
{
    public required string SubscriptionId { get; init; }
    public required int BuildId { get; init; }
    public required string TargetBranch { get; init; }
}

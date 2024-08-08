﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace ProductConstructionService.Api.Controllers.Models;

public class CreateBranchRequest
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
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Maestro.Api.Model.v2020_02_20;

public class CreateBranchRequest
{
    /// <summary>
    /// Subscription that is being flown/triggered.
    /// </summary>
    public Guid SubscriptionId { get; init; }

    /// <summary>
    /// Build that is being flown.
    /// </summary>
    public int BuildId { get; init; }

    /// <summary>
    /// Name of the PR branch that will be created in the target repo.
    /// </summary>
    public string PrBranch { get; init; }
}

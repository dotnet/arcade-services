// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;

namespace ProductConstructionService.DependencyFlow.PullRequestUpdaters;

public record SubscriptionUpdateResult(
    string OutcomeMessage,
    SubscriptionOutcomeType OutcomeType,
    string? PrUrl);

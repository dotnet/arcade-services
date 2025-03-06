// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.DependencyFlow.Model;

public enum SubscriptionUpdateAction
{
    NoNewUpdates = 0,
    ApplyingUpdates = 1,
    MergingPr = 2
}

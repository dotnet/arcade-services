// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.BarViz.Model;

/// <summary>
/// Content for the <c>SubscriptionDetailDialog</c>. Carries either a fully loaded
/// <see cref="Subscription"/> or just a subscription ID, in which case the dialog
/// loads the subscription itself and shows a loading indicator while doing so.
/// </summary>
public record SubscriptionDetailData
{
    public Guid SubscriptionId { get; private init; }

    public Subscription? Subscription { get; private init; }

    public static SubscriptionDetailData FromSubscription(Subscription subscription)
        => new() { SubscriptionId = subscription.Id, Subscription = subscription };

    public static SubscriptionDetailData FromId(Guid subscriptionId)
        => new() { SubscriptionId = subscriptionId };
}

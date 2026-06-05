// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.BarViz.Code.Helpers;

/// <summary>
///   Pairs a subscription with its latest (errored) trigger outcome for display in the error banner.
/// </summary>
public record SubscriptionOutcomeError(Subscription Subscription, SubscriptionTriggerOutcome Outcome);

/// <summary>
///   Helpers for surfacing subscriptions whose most recent trigger outcome was an error.
/// </summary>
public static class SubscriptionOutcomeHelper
{
    public static bool IsErrorOutcome(SubscriptionTriggerOutcome? outcome)
        => outcome is not null && outcome.Type is OutcomeType.Failure or OutcomeType.UserError;

    public static bool IsUserError(SubscriptionTriggerOutcome? outcome)
        => outcome?.Type is OutcomeType.UserError;

    public static List<SubscriptionOutcomeError> GetErroredSubscriptions(
        IEnumerable<CodeflowSubscriptionStatus?> flows)
    {
        var result = new List<SubscriptionOutcomeError>();
        var seen = new HashSet<Guid>();

        foreach (var flow in flows)
        {
            if (flow?.Subscription is { } subscription
                && IsErrorOutcome(flow.LatestOutcome)
                && seen.Add(subscription.Id))
            {
                result.Add(new SubscriptionOutcomeError(subscription, flow.LatestOutcome));
            }
        }

        return result;
    }
}

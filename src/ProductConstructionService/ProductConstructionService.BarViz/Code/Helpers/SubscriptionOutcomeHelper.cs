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

    /// <summary>
    ///   Tooltip for a single subscription row: the outcome message when the latest outcome is an error, else null (no icon).
    /// </summary>
    public static string? GetSubscriptionTooltip(SubscriptionTriggerOutcome? outcome)
        => IsErrorOutcome(outcome) ? DescribeOutcome(outcome) : null;

    /// <summary>
    ///   Builds the codeflow row alert-icon tooltip describing which flow direction(s) had an errored
    ///   latest trigger outcome. Returns null when neither flow is in error (no icon shown).
    /// </summary>
    public static string? GetCodeflowTooltip(
        SubscriptionTriggerOutcome? forwardFlowOutcome,
        SubscriptionTriggerOutcome? backflowOutcome)
    {
        var lines = new List<string>();

        if (IsErrorOutcome(forwardFlowOutcome))
        {
            lines.Add("Forward flow: " + DescribeOutcome(forwardFlowOutcome));
        }

        if (IsErrorOutcome(backflowOutcome))
        {
            lines.Add("Backflow: " + DescribeOutcome(backflowOutcome));
        }

        if (lines.Count == 0)
        {
            return null;
        }

        lines.Insert(0, "Failing codeflows:");
        return string.Join("\n", lines);
    }

    private static string DescribeOutcome(SubscriptionTriggerOutcome? outcome)
        => string.IsNullOrWhiteSpace(outcome?.Message) ? "the last trigger failed." : outcome.Message;

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

    public static List<SubscriptionOutcomeError> GetErroredSubscriptions(
        IEnumerable<Subscription> subscriptions,
        IReadOnlyDictionary<Guid, SubscriptionTriggerOutcome> latestOutcomes)
    {
        var result = new List<SubscriptionOutcomeError>();
        var seen = new HashSet<Guid>();

        foreach (var subscription in subscriptions)
        {
            if (subscription is not null
                && latestOutcomes.TryGetValue(subscription.Id, out var outcome)
                && IsErrorOutcome(outcome)
                && seen.Add(subscription.Id))
            {
                result.Add(new SubscriptionOutcomeError(subscription, outcome));
            }
        }

        return result;
    }
}

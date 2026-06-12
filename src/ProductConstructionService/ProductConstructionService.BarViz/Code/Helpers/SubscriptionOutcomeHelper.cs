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
    ///   Builds a short, human-readable blurb describing a subscription of the form "source → target"
    ///   using the short repository names. Returns null when either repository is missing
    ///   (e.g. the subscription no longer exists), so callers can fall back to another display.
    /// </summary>
    public static string? GetSubscriptionBlurb(string? sourceRepository, string? targetRepository, string targetBranch)
    {
        if (string.IsNullOrEmpty(sourceRepository)
            || string.IsNullOrEmpty(targetRepository)
            || string.IsNullOrEmpty(targetBranch))
        {
            return null;
        }

        var source = RepoShortName(sourceRepository);
        var target = RepoShortName(targetRepository);

        if (source == null || target == null)
        {
            return null;
        }

        return $"{source} → {target} ({targetBranch})";
    }

    /// <summary>
    ///   Converts a repository URL to a readable "org/repo" short name
    /// </summary>
    public static string? RepoShortName(string repoUrl)
    {
        var slug = RepoUrlConverter.RepoUrlToSlug(repoUrl);
        if (slug is null)
        {
            return null;
        }

        // Slugs look like "github:org:repo" or "azdo:org:project:repo"; drop the leading
        // repo-type segment and join the rest as a readable "org/repo" path.
        return string.Join('/', slug.Split(':').Skip(1));
    }

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

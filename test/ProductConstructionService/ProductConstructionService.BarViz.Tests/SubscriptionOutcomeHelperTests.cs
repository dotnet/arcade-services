// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;
using ProductConstructionService.BarViz.Code.Helpers;

namespace ProductConstructionService.BarViz.Tests;

[TestFixture]
public class SubscriptionOutcomeHelperTests
{
    private static Subscription CreateSubscription(Guid id)
        => new(
            id,
            true,
            true,
            "https://github.com/dotnet/runtime",
            "https://github.com/dotnet/dotnet",
            "main",
            string.Empty,
            "src/runtime",
            string.Empty,
            [])
        {
            Channel = new Channel(1, ".NET 10", "test")
        };

    private static SubscriptionTriggerOutcome CreateOutcome(Guid subscriptionId, OutcomeType type, DateTimeOffset? date = null)
        => new(
            subscriptionId,
            buildId: 1,
            date: date ?? DateTimeOffset.UtcNow,
            type: type,
            operationId: Guid.NewGuid().ToString("N"),
            message: $"{type} message");

    private static CodeflowSubscriptionStatus CreateFlow(Subscription subscription, SubscriptionTriggerOutcome? outcome)
        => new()
        {
            Subscription = subscription,
            LatestOutcome = outcome
        };

    [TestCase(OutcomeType.Failure, true)]
    [TestCase(OutcomeType.UserError, true)]
    [TestCase(OutcomeType.Updated, false)]
    [TestCase(OutcomeType.NoUpdate, false)]
    [TestCase(OutcomeType.NotUpdatable, false)]
    [TestCase(OutcomeType.HasConflict, false)]
    [TestCase(OutcomeType.Rescheduled, false)]
    public void IsErrorOutcome_OnlyTrueForFailureAndUserError(OutcomeType type, bool expected)
    {
        var outcome = CreateOutcome(Guid.NewGuid(), type);

        SubscriptionOutcomeHelper.IsErrorOutcome(outcome).Should().Be(expected);
    }

    [Test]
    public void IsErrorOutcome_ReturnsFalse_WhenOutcomeIsNull()
    {
        SubscriptionOutcomeHelper.IsErrorOutcome(null).Should().BeFalse();
    }

    [TestCase(OutcomeType.UserError, true)]
    [TestCase(OutcomeType.Failure, false)]
    public void IsUserError_OnlyTrueForUserError(OutcomeType type, bool expected)
    {
        var outcome = CreateOutcome(Guid.NewGuid(), type);

        SubscriptionOutcomeHelper.IsUserError(outcome).Should().Be(expected);
    }

    [Test]
    public void IsUserError_ReturnsFalse_WhenOutcomeIsNull()
    {
        SubscriptionOutcomeHelper.IsUserError(null).Should().BeFalse();
    }

    [Test]
    public void GetErroredSubscriptions_KeepsOnlyErrorOutcomes()
    {
        var failedId = Guid.NewGuid();
        var userErrorId = Guid.NewGuid();
        var updatedId = Guid.NewGuid();

        var flows = new CodeflowSubscriptionStatus?[]
        {
            CreateFlow(CreateSubscription(failedId), CreateOutcome(failedId, OutcomeType.Failure)),
            CreateFlow(CreateSubscription(userErrorId), CreateOutcome(userErrorId, OutcomeType.UserError)),
            CreateFlow(CreateSubscription(updatedId), CreateOutcome(updatedId, OutcomeType.Updated)),
        };

        var errors = SubscriptionOutcomeHelper.GetErroredSubscriptions(flows);

        errors.Select(e => e.Subscription.Id)
            .Should().BeEquivalentTo([failedId, userErrorId]);
    }

    [Test]
    public void GetErroredSubscriptions_IgnoresNullFlowsAndFlowsWithoutOutcome()
    {
        var failedId = Guid.NewGuid();
        var noOutcomeId = Guid.NewGuid();

        var flows = new CodeflowSubscriptionStatus?[]
        {
            null,
            CreateFlow(CreateSubscription(noOutcomeId), outcome: null),
            CreateFlow(CreateSubscription(failedId), CreateOutcome(failedId, OutcomeType.Failure)),
        };

        var errors = SubscriptionOutcomeHelper.GetErroredSubscriptions(flows);

        errors.Should().ContainSingle()
            .Which.Subscription.Id.Should().Be(failedId);
    }

    [Test]
    public void GetErroredSubscriptions_DeduplicatesBySubscriptionId()
    {
        // The same subscription can surface in more than one flow/row (e.g. forward + back).
        var subscriptionId = Guid.NewGuid();
        var subscription = CreateSubscription(subscriptionId);

        var flows = new CodeflowSubscriptionStatus?[]
        {
            CreateFlow(subscription, CreateOutcome(subscriptionId, OutcomeType.Failure)),
            CreateFlow(subscription, CreateOutcome(subscriptionId, OutcomeType.Failure)),
        };

        var errors = SubscriptionOutcomeHelper.GetErroredSubscriptions(flows);

        errors.Should().ContainSingle()
            .Which.Subscription.Id.Should().Be(subscriptionId);
    }

    [Test]
    public void GetErroredSubscriptions_ReturnsEmpty_WhenNoFlows()
    {
        SubscriptionOutcomeHelper.GetErroredSubscriptions([]).Should().BeEmpty();
    }
}

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

    [Test]
    public void GetErroredSubscriptions_FromDictionary_KeepsOnlyErrorOutcomes()
    {
        var failedId = Guid.NewGuid();
        var userErrorId = Guid.NewGuid();
        var updatedId = Guid.NewGuid();
        var noUpdateId = Guid.NewGuid();

        var subscriptions = new[]
        {
            CreateSubscription(failedId),
            CreateSubscription(userErrorId),
            CreateSubscription(updatedId),
            CreateSubscription(noUpdateId),
        };

        var latestOutcomes = new Dictionary<Guid, SubscriptionTriggerOutcome>
        {
            [failedId] = CreateOutcome(failedId, OutcomeType.Failure),
            [userErrorId] = CreateOutcome(userErrorId, OutcomeType.UserError),
            [updatedId] = CreateOutcome(updatedId, OutcomeType.Updated),
            [noUpdateId] = CreateOutcome(noUpdateId, OutcomeType.NoUpdate),
        };

        var errors = SubscriptionOutcomeHelper.GetErroredSubscriptions(subscriptions, latestOutcomes);

        errors.Select(e => e.Subscription.Id)
            .Should().BeEquivalentTo([failedId, userErrorId]);
    }

    [Test]
    public void GetErroredSubscriptions_FromDictionary_ExcludesSubscriptionsMissingFromDictionary()
    {
        var failedId = Guid.NewGuid();
        var missingId = Guid.NewGuid();

        var subscriptions = new[]
        {
            CreateSubscription(failedId),
            CreateSubscription(missingId),
        };

        var latestOutcomes = new Dictionary<Guid, SubscriptionTriggerOutcome>
        {
            [failedId] = CreateOutcome(failedId, OutcomeType.Failure),
        };

        var errors = SubscriptionOutcomeHelper.GetErroredSubscriptions(subscriptions, latestOutcomes);

        errors.Should().ContainSingle()
            .Which.Subscription.Id.Should().Be(failedId);
    }

    [Test]
    public void GetErroredSubscriptions_FromDictionary_PairsSubscriptionWithItsOutcome()
    {
        var failedId = Guid.NewGuid();
        var userErrorId = Guid.NewGuid();

        var subscriptions = new[]
        {
            CreateSubscription(failedId),
            CreateSubscription(userErrorId),
        };

        var failedOutcome = CreateOutcome(failedId, OutcomeType.Failure);
        var userErrorOutcome = CreateOutcome(userErrorId, OutcomeType.UserError);

        var latestOutcomes = new Dictionary<Guid, SubscriptionTriggerOutcome>
        {
            [failedId] = failedOutcome,
            [userErrorId] = userErrorOutcome,
        };

        var errors = SubscriptionOutcomeHelper.GetErroredSubscriptions(subscriptions, latestOutcomes);

        errors.Should().Contain(e => e.Subscription.Id == failedId)
            .Which.Outcome.OperationId.Should().Be(failedOutcome.OperationId);
        errors.Should().Contain(e => e.Subscription.Id == userErrorId)
            .Which.Outcome.OperationId.Should().Be(userErrorOutcome.OperationId);
    }

    [Test]
    public void GetErroredSubscriptions_FromDictionary_ReturnsEmpty_WhenNoSubscriptions()
    {
        var latestOutcomes = new Dictionary<Guid, SubscriptionTriggerOutcome>();

        SubscriptionOutcomeHelper.GetErroredSubscriptions([], latestOutcomes).Should().BeEmpty();
    }

    [Test]
    public void GetErroredSubscriptions_FromDictionary_DeduplicatesBySubscriptionId()
    {
        // A subscription id should only surface once even if it appears twice in the input list.
        var subscriptionId = Guid.NewGuid();
        var subscription = CreateSubscription(subscriptionId);

        var subscriptions = new[] { subscription, subscription };

        var latestOutcomes = new Dictionary<Guid, SubscriptionTriggerOutcome>
        {
            [subscriptionId] = CreateOutcome(subscriptionId, OutcomeType.Failure),
        };

        var errors = SubscriptionOutcomeHelper.GetErroredSubscriptions(subscriptions, latestOutcomes);

        errors.Should().ContainSingle()
            .Which.Subscription.Id.Should().Be(subscriptionId);
    }

    [Test]
    public void GetSubscriptionTooltip_ReturnsNull_WhenOutcomeIsNull()
    {
        SubscriptionOutcomeHelper.GetSubscriptionTooltip(null).Should().BeNull();
    }

    [TestCase(OutcomeType.Updated)]
    [TestCase(OutcomeType.NoUpdate)]
    public void GetSubscriptionTooltip_ReturnsNull_ForNonErrorOutcomes(OutcomeType type)
    {
        var outcome = CreateOutcome(Guid.NewGuid(), type);

        SubscriptionOutcomeHelper.GetSubscriptionTooltip(outcome).Should().BeNull();
    }

    [TestCase(OutcomeType.Failure)]
    [TestCase(OutcomeType.UserError)]
    public void GetSubscriptionTooltip_ReturnsMessage_ForErrorOutcomes(OutcomeType type)
    {
        var outcome = CreateOutcome(Guid.NewGuid(), type);

        SubscriptionOutcomeHelper.GetSubscriptionTooltip(outcome).Should().Be(outcome.Message);
    }

    [Test]
    public void GetSubscriptionTooltip_ReturnsFallback_WhenErrorMessageIsEmpty()
    {
        var outcome = new SubscriptionTriggerOutcome(
            Guid.NewGuid(),
            buildId: 1,
            date: DateTimeOffset.UtcNow,
            type: OutcomeType.Failure,
            operationId: Guid.NewGuid().ToString("N"),
            message: "");

        SubscriptionOutcomeHelper.GetSubscriptionTooltip(outcome).Should().Be("the last trigger failed.");
    }

    [Test]
    public void GetCodeflowTooltip_ReturnsNull_WhenBothOutcomesAreNull()
    {
        SubscriptionOutcomeHelper.GetCodeflowTooltip(null, null).Should().BeNull();
    }

    [Test]
    public void GetCodeflowTooltip_ReturnsNull_WhenBothOutcomesAreNonErrors()
    {
        var forward = CreateOutcome(Guid.NewGuid(), OutcomeType.Updated);
        var backflow = CreateOutcome(Guid.NewGuid(), OutcomeType.NoUpdate);

        SubscriptionOutcomeHelper.GetCodeflowTooltip(forward, backflow).Should().BeNull();
    }

    [Test]
    public void GetCodeflowTooltip_ReturnsForwardLine_WhenOnlyForwardOutcomeIsError()
    {
        var forward = CreateOutcome(Guid.NewGuid(), OutcomeType.Failure);
        var backflow = CreateOutcome(Guid.NewGuid(), OutcomeType.Updated);

        SubscriptionOutcomeHelper.GetCodeflowTooltip(forward, backflow)
            .Should().Be("Failing codeflows:\nForward flow: " + forward.Message);
    }

    [Test]
    public void GetCodeflowTooltip_ReturnsBackflowLine_WhenOnlyBackflowOutcomeIsError()
    {
        var forward = CreateOutcome(Guid.NewGuid(), OutcomeType.Updated);
        var backflow = CreateOutcome(Guid.NewGuid(), OutcomeType.Failure);

        SubscriptionOutcomeHelper.GetCodeflowTooltip(forward, backflow)
            .Should().Be("Failing codeflows:\nBackflow: " + backflow.Message);
    }

    [Test]
    public void GetCodeflowTooltip_ReturnsBothLinesInOrder_WhenBothOutcomesAreErrors()
    {
        var forward = CreateOutcome(Guid.NewGuid(), OutcomeType.Failure);
        var backflow = CreateOutcome(Guid.NewGuid(), OutcomeType.UserError);

        SubscriptionOutcomeHelper.GetCodeflowTooltip(forward, backflow)
            .Should().Be("Failing codeflows:\nForward flow: " + forward.Message + "\nBackflow: " + backflow.Message);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow.Tests
{
    [TestFixture]
    public class ForceUpdateFunctionalityTests
    {
        [Test]
        public void SubscriptionTriggerWorkItem_ShouldSupportForceParameter()
        {
            // Arrange
            var subscriptionId = Guid.NewGuid();
            var buildId = 123;
            var force = true;

            // Act
            var workItem = new SubscriptionTriggerWorkItem
            {
                SubscriptionId = subscriptionId,
                BuildId = buildId,
                Force = force
            };

            // Assert
            workItem.SubscriptionId.Should().Be(subscriptionId);
            workItem.BuildId.Should().Be(buildId);
            workItem.Force.Should().Be(force);
        }

        [Test]
        public void SubscriptionTriggerWorkItem_ForceDefaultsToFalse()
        {
            // Arrange & Act
            var workItem = new SubscriptionTriggerWorkItem
            {
                SubscriptionId = Guid.NewGuid(),
                BuildId = 123
            };

            // Assert - Force should default to false when not explicitly set
            workItem.Force.Should().BeFalse();
        }
    }
}
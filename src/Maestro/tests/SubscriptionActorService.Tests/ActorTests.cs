// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using ServiceFabricMocks;

namespace SubscriptionActorService.Tests
{
    [TestFixture]
    public abstract class ActorTests : TestsWithServices
    {
        protected Dictionary<string, object> ExpectedActorState;

        protected Dictionary<string, MockReminderManager.Reminder> ExpectedReminders;

        protected MockReminderManager Reminders;
        protected MockActorStateManager StateManager;

        [SetUp]
        public void ActorTests_SetUp()
        {
            ExpectedActorState = new Dictionary<string, object>();
            ExpectedReminders = new Dictionary<string, MockReminderManager.Reminder>();
            StateManager = new MockActorStateManager();
            Reminders = new MockReminderManager();
        }

        [TearDown]
        public void ActorTests_TearDown()
        {
            Reminders.Data.Should().BeEquivalentTo(ExpectedReminders);
            StateManager.Data.Should().BeEquivalentTo(ExpectedActorState);
        }
    }
}

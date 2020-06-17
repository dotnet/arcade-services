// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using FluentAssertions;
using ServiceFabricMocks;

namespace SubscriptionActorService.Tests
{
    public class ActorTests : TestsWithServices
    {
        protected readonly Dictionary<string, object> ExpectedActorState = new Dictionary<string, object>();

        protected readonly Dictionary<string, MockReminderManager.Reminder> ExpectedReminders =
            new Dictionary<string, MockReminderManager.Reminder>();

        protected readonly MockReminderManager Reminders;
        protected readonly MockActorStateManager StateManager;

        protected ActorTests()
        {
            StateManager = new MockActorStateManager();
            Reminders = new MockReminderManager();
        }

        public override void Dispose()
        {
            Reminders.Data.Should().BeEquivalentTo(ExpectedReminders);
            StateManager.Data.Should().BeEquivalentTo(ExpectedActorState);
            base.Dispose();
        }
    }
}

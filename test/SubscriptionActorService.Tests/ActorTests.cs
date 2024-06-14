// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using ServiceFabricMocks;

namespace SubscriptionActorService.Tests;

[TestFixture]
public abstract class ActorTests : TestsWithServices
{
    protected Dictionary<string, object> ExpectedActorState = null!;

    protected Dictionary<string, MockReminderManager.Reminder> ExpectedReminders = null!;

    protected MockReminderManager Reminders = null!;
    protected MockActorStateManager StateManager = null!;

    [SetUp]
    public void ActorTests_SetUp()
    {
        ExpectedActorState = [];
        ExpectedReminders = [];
        StateManager = new MockActorStateManager();
        Reminders = new MockReminderManager();
    }

    [TearDown]
    public void ActorTests_TearDown()
    {
        Reminders.Data.Should().BeEquivalentTo(ExpectedReminders, options => options.ExcludingProperties());
        StateManager.Data.Should().BeEquivalentTo(ExpectedActorState);
    }
}

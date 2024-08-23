// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
internal abstract class ActorTests : TestsWithServices
{
    protected Dictionary<string, object> ExpectedActorState = null!;

    protected Dictionary<string, MockReminderManager> ExpectedReminders = null!;

    protected MockReminderManagerFactory Reminders = null!;
    protected MockRedisCacheFactory RedisCache = null!;

    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);
        services.AddSingleton(RedisCache);
        services.AddSingleton(Reminders);
    }

    [SetUp]
    public void ActorTests_SetUp()
    {
        ExpectedActorState = [];
        ExpectedReminders = [];
        RedisCache = new();
        Reminders = new();
    }

    [TearDown]
    public void ActorTests_TearDown()
    {
        Reminders.Reminders.Should().BeEquivalentTo(ExpectedReminders, options => options.ExcludingProperties());
        RedisCache.Data.Should().BeEquivalentTo(ExpectedActorState);
    }
}

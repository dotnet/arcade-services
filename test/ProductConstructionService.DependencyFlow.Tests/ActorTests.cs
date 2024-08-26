// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Maestro.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
internal abstract class ActorTests : TestsWithServices
{
    protected Dictionary<string, object> ExpectedActorState = null!;

    protected Dictionary<string, object> ExpectedReminders = null!;

    protected MockReminderManagerFactory Reminders = null!;
    protected MockRedisCacheFactory RedisCache = null!;

    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);
        services.AddSingleton(RedisCache);
        services.AddSingleton(Reminders);
        services.AddDependencyFlowProcessors();
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

    protected void SetReminder<T>(Subscription subscription, T reminder) where T : WorkItem
    {
        Reminders.Reminders[typeof(T).Name + "_" + GetPullRequestActorId(subscription)] = reminder;
    }

    protected void RemoveReminder<T>(Subscription subscription) where T : WorkItem
    {
        Reminders.Reminders.Remove(typeof(T).Name + "_" + GetPullRequestActorId(subscription));
    }

    protected void SetState<T>(Subscription subscription, T state) where T : class
    {
        RedisCache.Data[typeof(T).Name + "_" + GetPullRequestActorId(subscription)] = state;
    }

    protected void RemoveState<T>(Subscription subscription) where T : class
    {
        RedisCache.Data.Remove(typeof(T).Name + "_" + GetPullRequestActorId(subscription));
    }

    protected void SetExpectedReminder<T>(Subscription subscription, T reminder) where T : WorkItem
    {
        ExpectedReminders[typeof(T).Name + "_" + GetPullRequestActorId(subscription)] = reminder;
    }

    protected void RemoveExpectedReminder<T>(Subscription subscription) where T : WorkItem
    {
        ExpectedReminders.Remove(typeof(T).Name + "_" + GetPullRequestActorId(subscription));
    }

    protected void SetExpectedState<T>(Subscription subscription, T state) where T : class
    {
        ExpectedActorState[typeof(T).Name + "_" + GetPullRequestActorId(subscription)] = state;
    }

    protected void RemoveExpectedState<T>(Subscription subscription) where T : class
    {
        ExpectedActorState.Remove(typeof(T).Name + "_" + GetPullRequestActorId(subscription));
    }

    protected static PullRequestActorId GetPullRequestActorId(Subscription subscription)
    {
        return subscription.PolicyObject.Batchable
            ? new BatchedPullRequestActorId(subscription.TargetRepository, subscription.TargetBranch)
            : new NonBatchedPullRequestActorId(subscription.Id);
    }
}

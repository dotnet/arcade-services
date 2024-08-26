// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Storage.Queues.Models;
using FluentAssertions;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using NUnit.Framework;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
internal abstract class ActorTests : TestsWithServices
{
    protected const string AssetFeedUrl = "https://source.feed/index.json";
    protected const string SourceBranch = "source.branch";
    protected const string SourceRepo = "source.repo";
    protected const string TargetRepo = "target.repo";
    protected const string TargetBranch = "target.branch";
    protected const string NewBuildNumber = "build.number";
    protected const string NewCommit = "sha2";

    protected Dictionary<string, object> ExpectedActorState { get; private set; } = null!;
    protected Dictionary<string, object> ExpectedReminders { get; private set; } = null!;

    protected MockReminderManagerFactory Reminders { get; private set; } = null!;
    protected MockRedisCacheFactory RedisCache { get; private set; } = null!;

    protected Mock<IRemoteFactory> RemoteFactory { get; private set; } = null!;
    protected Dictionary<string, Mock<IRemote>> DarcRemotes { get; private set; } = null!;
    protected Mock<ICoherencyUpdateResolver> UpdateResolver { get; private set; } = null!;
    protected Mock<IMergePolicyEvaluator> MergePolicyEvaluator { get; private set; } = null!;

    protected List<CodeFlowWorkItem> CodeFlowWorkItemsProduced { get; private set; } = null!;


    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);
        services.AddDependencyFlowProcessors();
        services.AddSingleton<IRedisCacheFactory>(RedisCache);
        services.AddSingleton<IReminderManagerFactory>(Reminders);
        services.AddOperationTracking(_ => { });
        services.AddSingleton<ExponentialRetry>();
        services.AddSingleton(Mock.Of<IPullRequestPolicyFailureNotifier>());
        services.AddSingleton(Mock.Of<IKustoClientProvider>());
        services.AddSingleton(RemoteFactory.Object);
        services.AddSingleton(MergePolicyEvaluator.Object);
        services.AddSingleton(UpdateResolver.Object);
        services.AddLogging();

        // TODO (https://github.com/dotnet/arcade-services/issues/3866): Can be removed once we execute code flow directly
        // (when we remove producer factory from the constructor)
        Mock<IWorkItemProducerFactory> workItemProducerFactoryMock = new();
        Mock<IWorkItemProducer<CodeFlowWorkItem>> workItemProducerMock = new();
        workItemProducerMock.Setup(w => w.ProduceWorkItemAsync(It.IsAny<CodeFlowWorkItem>(), TimeSpan.Zero))
            .ReturnsAsync(QueuesModelFactory.SendReceipt("message", DateTimeOffset.Now, DateTimeOffset.Now, "popReceipt", DateTimeOffset.Now))
            .Callback<CodeFlowWorkItem, TimeSpan>((item, _) => CodeFlowWorkItemsProduced.Add(item));
        workItemProducerFactoryMock.Setup(w => w.CreateProducer<CodeFlowWorkItem>())
            .Returns(workItemProducerMock.Object);
        services.AddSingleton(workItemProducerFactoryMock.Object);

        RemoteFactory
            .Setup(f => f.GetRemoteAsync(It.IsAny<string>(), It.IsAny<ILogger>()))
            .ReturnsAsync((string repo, ILogger logger) =>
                DarcRemotes.GetOrAddValue(repo, () => CreateMock<IRemote>()).Object);
    }

    [SetUp]
    public void ActorTests_SetUp()
    {
        ExpectedActorState = [];
        ExpectedReminders = [];
        RedisCache = new();
        Reminders = new();
        RemoteFactory = new(MockBehavior.Strict);
        DarcRemotes = new()
        {
            [TargetRepo] = new Mock<IRemote>()
        };
        MergePolicyEvaluator = CreateMock<IMergePolicyEvaluator>();
        UpdateResolver = new(MockBehavior.Strict);
        CodeFlowWorkItemsProduced = [];
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

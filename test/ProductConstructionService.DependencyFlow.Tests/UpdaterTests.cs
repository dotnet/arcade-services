// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Internal.Logging;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using NUnit.Framework;
using ProductConstructionService.Common;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
internal abstract class UpdaterTests : TestsWithServices
{
    protected const string AssetFeedUrl = "https://source.feed/index.json";
    protected const string SourceBranch = "source.branch";
    protected const string SourceRepo = "https://github.com/foo/bar/";
    protected const string TargetRepo = "target.repo";
    protected const string TargetBranch = "target.branch";
    protected const string NewBuildNumber = "build.number";
    protected const string NewCommit = "sha123456";
    protected const string VmrPath = "D:\\vmr";
    protected const string TmpPath = "D:\\tmp";
    protected const string VmrUri = "https://github.com/maestro-auth-test/dnceng-vmr";
    protected string VmrPullRequestUrl = $"{VmrUri}/pulls/1";

    protected Dictionary<string, object> ExpectedCacheState { get; private set; } = null!;
    protected Dictionary<string, object> ExpectedReminders { get; private set; } = null!;

    protected MockReminderManagerFactory Reminders { get; private set; } = null!;
    protected MockRedisCacheFactory Cache { get; private set; } = null!;

    protected Mock<IRemoteFactory> RemoteFactory { get; private set; } = null!;
    protected Dictionary<string, Mock<IRemote>> DarcRemotes { get; private set; } = null!;
    protected Mock<ICoherencyUpdateResolver> UpdateResolver { get; private set; } = null!;
    protected Mock<IMergePolicyEvaluator> MergePolicyEvaluator { get; private set; } = null!;

    protected override void RegisterServices(IServiceCollection services)
    {
        base.RegisterServices(services);
        services.AddDependencyFlowProcessors();
        services.AddSingleton<IRedisCacheFactory>(Cache);
        services.AddSingleton<IReminderManagerFactory>(Reminders);
        services.AddOperationTracking(_ => { });
        services.AddSingleton<ExponentialRetry>();
        services.AddSingleton(Mock.Of<IPullRequestPolicyFailureNotifier>());
        services.AddSingleton(Mock.Of<IKustoClientProvider>());
        services.AddSingleton(RemoteFactory.Object);
        services.AddSingleton(MergePolicyEvaluator.Object);
        services.AddSingleton(UpdateResolver.Object);
        services.AddLogging();

        RemoteFactory
            .Setup(f => f.CreateRemoteAsync(It.IsAny<string>()))
            .ReturnsAsync((string repo) => DarcRemotes.GetOrAddValue(repo, () => CreateMock<IRemote>()).Object);
    }

    [SetUp]
    public void UpdaterTests_SetUp()
    {
        ExpectedCacheState = [];
        ExpectedReminders = [];
        Cache = new();
        Reminders = new();
        RemoteFactory = new();
        DarcRemotes = new()
        {
            [TargetRepo] = new Mock<IRemote>()
        };
        MergePolicyEvaluator = new();
        UpdateResolver = new();
    }

    [TearDown]
    public void UpdaterTests_TearDown()
    {
        foreach (var pair in Cache.Data)
        {
            if (pair.Value is InProgressPullRequest pr)
            {
                pr.LastCheck = (ExpectedCacheState[pair.Key] as InProgressPullRequest)!.LastCheck;
                pr.LastUpdate = (ExpectedCacheState[pair.Key] as InProgressPullRequest)!.LastUpdate;
                pr.NextCheck = (ExpectedCacheState[pair.Key] as InProgressPullRequest)!.NextCheck;
            }
        }
        Cache.Data.Should().BeEquivalentTo(ExpectedCacheState);
        Reminders.Reminders.Should().BeEquivalentTo(ExpectedReminders);
    }

    protected void SetState<T>(Subscription subscription, T state) where T : class
    {
        Cache.Data[typeof(T).Name + "_" + GetPullRequestUpdaterId(subscription)] = state;
    }

    protected void RemoveState<T>(Subscription subscription) where T : class
    {
        Cache.Data.Remove(typeof(T).Name + "_" + GetPullRequestUpdaterId(subscription));
    }

    protected void SetExpectedReminder<T>(Subscription subscription, T reminder) where T : WorkItem
    {
        ExpectedReminders[typeof(T).Name + "_" + GetPullRequestUpdaterId(subscription)] = reminder;
    }

    protected void RemoveExpectedReminder<T>(Subscription subscription) where T : WorkItem
    {
        ExpectedReminders.Remove(typeof(T).Name + "_" + GetPullRequestUpdaterId(subscription));
    }

    protected void SetExpectedState<T>(Subscription subscription, T state) where T : class
    {
        ExpectedCacheState[typeof(T).Name + "_" + GetPullRequestUpdaterId(subscription)] = state;
    }

    protected void RemoveExpectedState<T>(Subscription subscription) where T : class
    {
        ExpectedCacheState.Remove(typeof(T).Name + "_" + GetPullRequestUpdaterId(subscription));
    }

    protected static PullRequestUpdaterId GetPullRequestUpdaterId(Subscription subscription)
    {
        return subscription.PolicyObject.Batchable
            ? new BatchedPullRequestUpdaterId(subscription.TargetRepository, subscription.TargetBranch)
            : new NonBatchedPullRequestUpdaterId(subscription.Id);
    }
}

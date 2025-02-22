// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using FluentAssertions;
using ProductConstructionService.Api.v2020_02_20.Models;
using Maestro.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using ProductConstructionService.Api.Api.v2020_02_20.Controllers;
using ProductConstructionService.WorkItems;
using ProductConstructionService.DependencyFlow.WorkItems;
using Microsoft.DotNet.DarcLib.Helpers;
using ProductConstructionService.Api.Api;

namespace ProductConstructionService.Api.Tests;

[TestFixture]
public partial class SubscriptionsController20200220Tests : IDisposable
{
    private readonly TestData _data;
    private static readonly Mock<IGitHubInstallationIdResolver> _mockInstallationIdResolver = new();

    public SubscriptionsController20200220Tests()
    {
        _data = TestData.Default.Build();
    }

    public void Dispose()
    {
        _data.Dispose();
    }

    [Test]
    public async Task CreateGetAndListSubscriptions()
    {
        var testChannelName = "test-channel-sub-controller20200220";
        var defaultGitHubSourceRepo = "https://github.com/dotnet/sub-controller-test-source-repo";
        var defaultGitHubTargetRepo = "https://github.com/dotnet/sub-controller-test-target-repo";
        var defaultAzdoSourceRepo = "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-source-repo";
        var defaultAzdoTargetRepo = "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-target-repo";
        var branchName1 = "a";
        var branchName2 = "b";
        var aValidDependencyFlowNotificationList = "@someMicrosoftUser;@some-github-team";

        // Create two subscriptions
        var subscription1 = new SubscriptionData()
        {
            ChannelName = testChannelName,
            Enabled = true,
            SourceRepository = defaultGitHubSourceRepo,
            TargetRepository = defaultGitHubTargetRepo,
            Policy = new v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = branchName1,
            PullRequestFailureNotificationTags = aValidDependencyFlowNotificationList
        };

        Subscription createdSubscription1;
        {
            IActionResult result = await _data.SubscriptionsController.Create(subscription1);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.Created);
            objResult.Value.Should().BeAssignableTo<Subscription>();
            createdSubscription1 = (Subscription)objResult.Value!;
            createdSubscription1.Channel.Name.Should().Be(testChannelName);
            createdSubscription1.Policy.Batchable.Should().Be(true);
            createdSubscription1.Policy.UpdateFrequency.Should().Be(v2018_07_16.Models.UpdateFrequency.EveryWeek);
            createdSubscription1.TargetBranch.Should().Be(branchName1);
            createdSubscription1.SourceRepository.Should().Be(defaultGitHubSourceRepo);
            createdSubscription1.TargetRepository.Should().Be(defaultGitHubTargetRepo);
            createdSubscription1.PullRequestFailureNotificationTags.Should().Be(aValidDependencyFlowNotificationList);
            createdSubscription1.SourceEnabled.Should().BeFalse();
            createdSubscription1.ExcludedAssets.Should().BeEmpty();
        }

        var subscription2 = new SubscriptionData()
        {
            ChannelName = testChannelName,
            Enabled = false,
            SourceRepository = defaultAzdoSourceRepo,
            TargetRepository = defaultAzdoTargetRepo,
            Policy = new v2018_07_16.Models.SubscriptionPolicy() { Batchable = false, UpdateFrequency = v2018_07_16.Models.UpdateFrequency.None },
            TargetBranch = branchName2,
            SourceEnabled = true,
            SourceDirectory = "sub-controller-test-source-repo",
            ExcludedAssets = [DependencyFileManager.ArcadeSdkPackageName, "Foo.Bar"],
        };

        Subscription createdSubscription2;
        {
            IActionResult result = await _data.SubscriptionsController.Create(subscription2);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.Created);
            objResult.Value.Should().BeAssignableTo<Subscription>();
            createdSubscription2 = (Subscription)objResult.Value!;
            createdSubscription2.Channel.Name.Should().Be(testChannelName);
            createdSubscription2.Policy.Batchable.Should().Be(false);
            createdSubscription2.Policy.UpdateFrequency.Should().Be(v2018_07_16.Models.UpdateFrequency.None);
            createdSubscription2.TargetBranch.Should().Be(branchName2);
            createdSubscription2.SourceRepository.Should().Be(defaultAzdoSourceRepo);
            createdSubscription2.TargetRepository.Should().Be(defaultAzdoTargetRepo);
            createdSubscription2.PullRequestFailureNotificationTags.Should().BeNull();
            createdSubscription2.SourceEnabled.Should().BeTrue();
            createdSubscription2.SourceDirectory.Should().Be("sub-controller-test-source-repo");
            createdSubscription2.ExcludedAssets.Should().BeEquivalentTo([DependencyFileManager.ArcadeSdkPackageName, "Foo.Bar"]);
        }

        // List all (both) subscriptions, spot check that we got both
        {
            IActionResult result = _data.SubscriptionsController.ListSubscriptions();
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            var listedSubs = ((IEnumerable<Subscription>)objResult.Value!).OrderBy(sub => sub.TargetBranch).ToList();
            listedSubs.Count.Should().Be(2);
            listedSubs[0].Enabled.Should().Be(true);
            listedSubs[0].TargetRepository.Should().Be(defaultGitHubTargetRepo);
            listedSubs[0].PullRequestFailureNotificationTags.Should().Be(aValidDependencyFlowNotificationList);
            listedSubs[0].ExcludedAssets.Should().BeEmpty();
            listedSubs[1].Enabled.Should().Be(false);
            listedSubs[1].TargetRepository.Should().Be(defaultAzdoTargetRepo);
            listedSubs[1].PullRequestFailureNotificationTags.Should().BeNull();
            listedSubs[1].ExcludedAssets.Should().BeEquivalentTo([DependencyFileManager.ArcadeSdkPackageName, "Foo.Bar"]);
        }
        // Use ListSubscriptions() params at least superficially to go down those codepaths
        {
            IActionResult result = _data.SubscriptionsController.ListSubscriptions(defaultAzdoSourceRepo, defaultAzdoTargetRepo, createdSubscription2.Channel.Id, enabled: false, sourceEnabled: true);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            var listedSubs = ((IEnumerable<Subscription>)objResult.Value!).ToList();
            listedSubs.Count.Should().Be(1);
            listedSubs[0].Enabled.Should().Be(false);
            listedSubs[0].TargetRepository.Should().Be(defaultAzdoTargetRepo);
            listedSubs[0].PullRequestFailureNotificationTags.Should().BeNull(); // This is sub2
            listedSubs[0].ExcludedAssets.Should().BeEquivalentTo([DependencyFileManager.ArcadeSdkPackageName, "Foo.Bar"]);
        }
        // Directly get one of the subscriptions
        {
            IActionResult result = await _data.SubscriptionsController.GetSubscription(createdSubscription1.Id);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            var theSubscription = (Subscription)objResult.Value!;
            theSubscription.Enabled.Should().Be(true);
            theSubscription.TargetRepository.Should().Be(defaultGitHubTargetRepo);
            theSubscription.PullRequestFailureNotificationTags.Should().Be(aValidDependencyFlowNotificationList);
            theSubscription.ExcludedAssets.Should().BeEmpty();
        }
    }

    [Test]
    public async Task GetAndListNonexistentSubscriptions()
    {
        var shouldntExist = Guid.Parse("00000000-0000-0000-0000-000000000042");

        // No subs added, get a random Guid
        {
            IActionResult result = await _data.SubscriptionsController.GetSubscription(shouldntExist);
            result.Should().BeAssignableTo<NotFoundResult>();
            var notFoundResult = (NotFoundResult)result;
            notFoundResult.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        }

        {
            IActionResult result = _data.SubscriptionsController.ListSubscriptions(
                "https://github.com/dotnet/does-not-exist",
                "https://github.com/dotnet/does-not-exist-2",
                123456,
                true);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            var listedSubs = ((IEnumerable<Subscription>)objResult.Value!).ToList();
            listedSubs.Should().BeEmpty();
        }
    }

    [Test]
    public async Task CreateSubscriptionForNonMicrosoftUserFails()
    {
        var testChannelName = "test-channel-sub-controller20200220";
        var defaultGitHubSourceRepo = "https://github.com/dotnet/sub-controller-test-source-repo";
        var defaultGitHubTargetRepo = "https://github.com/dotnet/sub-controller-test-target-repo";
        var defaultBranchName = "main";
        var anInvalidDependencyFlowNotificationList = "@someexternaluser;@somemicrosoftuser;@some-team";

        // @someexternaluser will resolve as not in the microsoft org and should fail
        var subscription = new SubscriptionData()
        {
            ChannelName = testChannelName,
            Enabled = true,
            SourceRepository = defaultGitHubSourceRepo,
            TargetRepository = defaultGitHubTargetRepo,
            Policy = new v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = defaultBranchName,
            PullRequestFailureNotificationTags = anInvalidDependencyFlowNotificationList
        };

        IActionResult result = await _data.SubscriptionsController.Create(subscription);
        result.Should().BeAssignableTo<BadRequestObjectResult>();
    }

    [Test]
    public async Task CreateSubscriptionForNonExistentChannelFails()
    {
        var defaultGitHubSourceRepo = "https://github.com/dotnet/sub-controller-test-source-repo";
        var defaultGitHubTargetRepo = "https://github.com/dotnet/sub-controller-test-target-repo";
        var defaultBranchName = "main";

        // Create two subscriptions
        var subscription = new SubscriptionData()
        {
            ChannelName = "this-channel-does-not-exist",
            Enabled = true,
            SourceRepository = defaultGitHubSourceRepo,
            TargetRepository = defaultGitHubTargetRepo,
            Policy = new v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = defaultBranchName
        };

        IActionResult result = await _data.SubscriptionsController.Create(subscription);
        result.Should().BeAssignableTo<BadRequestObjectResult>();
    }

    [Test]
    public async Task DeleteSubscription()
    {
        var testChannelName = "test-channel-sub-controller20200220";
        var deleteScenarioSourceRepo = "https://github.com/dotnet/sub-controller-delete-sub-source-repo";
        var deleteScenarioTargetRepo = "https://github.com/dotnet/sub-controller-delete-sub-target-repo";
        var defaultBranchName = "main";

        // Create two subscriptions
        var subscriptionToDelete = new SubscriptionData()
        {
            ChannelName = testChannelName,
            Enabled = true,
            SourceRepository = deleteScenarioSourceRepo,
            TargetRepository = deleteScenarioTargetRepo,
            Policy = new v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = defaultBranchName
        };

        {
            IActionResult createResult = await _data.SubscriptionsController.Create(subscriptionToDelete);
            createResult.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)createResult;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.Created);
            var createdSubscription = (Subscription)objResult.Value!;

            IActionResult deleteResult = await _data.SubscriptionsController.DeleteSubscription(createdSubscription.Id);
            deleteResult.Should().BeAssignableTo<OkObjectResult>();
            var deleteObjResult = (OkObjectResult)deleteResult;
            // Seems like this should be OK but it gives created... 
            deleteObjResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task CreateSubscriptionForUnregisteredRepositorySucceeds()
    {
        var defaultGitHubTargetRepo = "https://github.com/dotnet/repo-that-is-not-registered";

        // @someexternaluser will resolve as not in the microsoft org and should fail
        var subscription = new SubscriptionData()
        {
            ChannelName = "test-channel-sub-controller20200220",
            Enabled = true,
            SourceRepository = "https://github.com/dotnet/sub-controller-test-source-repo",
            TargetRepository = defaultGitHubTargetRepo,
            Policy = new v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = "main"
        };

        _mockInstallationIdResolver
            .Setup(x => x.GetInstallationIdForRepository(subscription.TargetRepository))
            .ReturnsAsync(451);

        IActionResult createdResult = await _data.SubscriptionsController.Create(subscription);
        createdResult.Should().BeAssignableTo<ObjectResult>();
        var objResult = (ObjectResult)createdResult;
        objResult.StatusCode.Should().Be((int)HttpStatusCode.Created);
        var createdSubscription = (Subscription)objResult.Value!;

        // Verify the subscription has been added
        {
            var getResult = await _data.SubscriptionsController.GetSubscription(createdSubscription.Id);
            getResult.Should().BeAssignableTo<ObjectResult>();
            objResult = (ObjectResult)getResult;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            var theSubscription = (Subscription)objResult.Value!;
            theSubscription.Enabled.Should().Be(true);
            theSubscription.Channel.Name.Should().Be(subscription.ChannelName);
            theSubscription.SourceRepository.Should().Be(subscription.SourceRepository);
            theSubscription.TargetRepository.Should().Be(subscription.TargetRepository);
            theSubscription.Policy.Batchable.Should().Be(true);
            theSubscription.Policy.UpdateFrequency.Should().Be(v2018_07_16.Models.UpdateFrequency.EveryWeek);
            theSubscription.ExcludedAssets.Should().BeEmpty();
        }
    }

    [Test]
    public async Task CreateSubscriptionForRepositoryWithoutAppInstallationFails()
    {
        // @someexternaluser will resolve as not in the microsoft org and should fail
        var subscription = new SubscriptionData()
        {
            ChannelName = "test-channel-sub-controller20200220",
            Enabled = true,
            SourceRepository = "https://github.com/dotnet/sub-controller-test-source-repo",
            TargetRepository = "https://github.com/dotnet/repo-that-has-no-installation",
            Policy = new v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = "main"
        };

        _mockInstallationIdResolver
            .Setup(x => x.GetInstallationIdForRepository(subscription.TargetRepository))
            .ReturnsAsync((long?)null);

        IActionResult createdResult = await _data.SubscriptionsController.Create(subscription);
        createdResult.Should().BeAssignableTo<ObjectResult>();
        var objResult = (ObjectResult)createdResult;
        objResult.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateSubscriptionForAzDoRepositoryWithoutInstallationSucceeds()
    {
        // @someexternaluser will resolve as not in the microsoft org and should fail
        var subscription = new SubscriptionData()
        {
            ChannelName = "test-channel-sub-controller20200220",
            Enabled = true,
            SourceRepository = "https://github.com/dotnet/sub-controller-test-source-repo",
            TargetRepository = "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-target-repo",
            Policy = new v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = "main"
        };

        _mockInstallationIdResolver
            .Setup(x => x.GetInstallationIdForRepository(subscription.TargetRepository))
            .ReturnsAsync((long?)null);

        IActionResult createdResult = await _data.SubscriptionsController.Create(subscription);
        createdResult.Should().BeAssignableTo<ObjectResult>();
        var objResult = (ObjectResult)createdResult;
        objResult.StatusCode.Should().Be((int)HttpStatusCode.Created);
    }

    [Test]
    public async Task TriggerSubscription()
    {
        var testChannelName = "test-channel-sub-controller20200220";
        var triggerScenarioSourceRepo = "https://github.com/dotnet/sub-controller-trigger-sub-source-repo";
        var triggerScenarioTargetRepo = "https://github.com/dotnet/sub-controller-trigger-sub-target-repo";
        var defaultBranchName = "main";

        // Create two subscriptions
        var subscriptionToTrigger = new SubscriptionData()
        {
            ChannelName = testChannelName,
            Enabled = true,
            SourceRepository = triggerScenarioSourceRepo,
            TargetRepository = triggerScenarioTargetRepo,
            Policy = new v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = defaultBranchName
        };

        Subscription createdSubscription;
        {
            IActionResult createResult = await _data.SubscriptionsController.Create(subscriptionToTrigger);
            createResult.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)createResult;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.Created);
            createdSubscription = (Subscription)objResult.Value!;
        }

        var build1Data = new BuildData()
        {
            GitHubRepository = triggerScenarioSourceRepo,
            AzureDevOpsBuildId = 123
        };
        var build2Data = new BuildData()
        {
            GitHubRepository = triggerScenarioSourceRepo,
            AzureDevOpsBuildId = 124
        };
        var build3Data = new BuildData()
        {
            GitHubRepository = $"{triggerScenarioSourceRepo}-different",
            AzureDevOpsBuildId = 125
        };
        Build build1, build3;
        // Add some builds
        {
            IActionResult createResult1 = await _data.BuildsController.Create(build1Data);
            createResult1.Should().BeAssignableTo<ObjectResult>();
            var objResult1 = (ObjectResult)createResult1;
            objResult1.StatusCode.Should().Be((int)HttpStatusCode.Created);
            build1 = (Build)objResult1.Value!;

            // Ignored build, just obviates the previous one.
            IActionResult createResult2 = await _data.BuildsController.Create(build2Data);
            createResult2.Should().BeAssignableTo<ObjectResult>();
            var objResult2 = (ObjectResult)createResult2;
            objResult2.StatusCode.Should().Be((int)HttpStatusCode.Created);

            IActionResult createResult3 = await _data.BuildsController.Create(build3Data);
            createResult3.Should().BeAssignableTo<ObjectResult>();
            var objResult3 = (ObjectResult)createResult3;
            objResult3.StatusCode.Should().Be((int)HttpStatusCode.Created);
            build3 = (Build)objResult3.Value!;
        }

        // Default scenario; 'trigger a subscription with latest build' codepath.
        {
            IActionResult triggerResult = await _data.SubscriptionsController.TriggerSubscription(createdSubscription.Id);
            triggerResult.Should().BeAssignableTo<AcceptedResult>();
            var latestTriggerResult = (AcceptedResult)triggerResult;
            latestTriggerResult.StatusCode.Should().Be((int)HttpStatusCode.Accepted);
        }

        // Scenario2: 'trigger a subscription with specific build' codepath.
        {
            IActionResult triggerResult = await _data.SubscriptionsController.TriggerSubscription(createdSubscription.Id, build1.Id);
            triggerResult.Should().BeAssignableTo<AcceptedResult>();
            var latestTriggerResult = (AcceptedResult)triggerResult;
            latestTriggerResult.StatusCode.Should().Be((int)HttpStatusCode.Accepted);
        }

        // Failure: Trigger a subscription with non-existent build id.
        {
            IActionResult triggerResult = await _data.SubscriptionsController.TriggerSubscription(createdSubscription.Id, 123456);
            triggerResult.Should().BeAssignableTo<BadRequestObjectResult>();
            var latestTriggerResult = (BadRequestObjectResult)triggerResult;
            latestTriggerResult.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        }

        // Failure: Trigger a subscription with non-existent build codepath.
        {
            IActionResult triggerResult = await _data.SubscriptionsController.TriggerSubscription(createdSubscription.Id, build3.Id);
            triggerResult.Should().BeAssignableTo<BadRequestObjectResult>();
            var latestTriggerResult = (BadRequestObjectResult)triggerResult;
            latestTriggerResult.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        }
    }

    [Test]
    public async Task UpdateSubscription()
    {
        var testChannelName = "test-channel-sub-controller20200220";
        var defaultGitHubSourceRepo = "https://github.com/dotnet/sub-controller-test-source-repo";
        var defaultGitHubTargetRepo = "https://github.com/dotnet/sub-controller-test-target-repo";
        var defaultBranchName = "main";
        var aValidDependencyFlowNotificationList = "@someMicrosoftUser;@some-github-team";
        var anInvalidDependencyFlowNotificationList = "@someExternalUser;@someMicrosoftUser;@some-team";

        // Create two subscriptions
        var subscription1 = new SubscriptionData()
        {
            ChannelName = testChannelName,
            Enabled = true,
            SourceRepository = $"{defaultGitHubSourceRepo}-needsupdate",
            TargetRepository = defaultGitHubTargetRepo,
            Policy = new v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = defaultBranchName
        };

        Subscription createdSubscription1;
        {
            IActionResult result = await _data.SubscriptionsController.Create(subscription1);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.Created);
            objResult.Value.Should().BeAssignableTo<Subscription>();
            createdSubscription1 = (Subscription)objResult.Value!;
            createdSubscription1.Channel.Name.Should().Be(testChannelName);
            createdSubscription1.Policy.Batchable.Should().Be(true);
            createdSubscription1.Policy.UpdateFrequency.Should().Be(v2018_07_16.Models.UpdateFrequency.EveryWeek);
            createdSubscription1.TargetBranch.Should().Be(defaultBranchName);
            createdSubscription1.SourceRepository.Should().Be($"{defaultGitHubSourceRepo}-needsupdate");
            createdSubscription1.TargetRepository.Should().Be(defaultGitHubTargetRepo);
        }

        var update = new SubscriptionUpdate()
        {
            Enabled = !subscription1.Enabled,
            Policy = new v2018_07_16.Models.SubscriptionPolicy() { Batchable = false, UpdateFrequency = v2018_07_16.Models.UpdateFrequency.EveryDay },
            SourceRepository = $"{subscription1.SourceRepository}-updated",
            PullRequestFailureNotificationTags = aValidDependencyFlowNotificationList
        };

        {
            IActionResult result = await _data.SubscriptionsController.UpdateSubscription(createdSubscription1.Id, update);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            objResult.Value.Should().BeAssignableTo<Subscription>();
            // Could also do a get after this; that more tests the underlying data context though.
            var updatedSubscription = (Subscription)objResult.Value!;
            updatedSubscription.Id.Should().Be(createdSubscription1.Id);
            updatedSubscription.Enabled.Should().Be(!subscription1.Enabled.Value);
            updatedSubscription.Policy.UpdateFrequency.Should().Be(v2018_07_16.Models.UpdateFrequency.EveryDay);
            updatedSubscription.SourceRepository.Should().Be($"{subscription1.SourceRepository}-updated");
            updatedSubscription.PullRequestFailureNotificationTags.Should().Be(aValidDependencyFlowNotificationList);
        }

        // Update with an invalid list, make sure it fails
        var badUpdate = new SubscriptionUpdate()
        {
            Enabled = !subscription1.Enabled,
            Policy = new v2018_07_16.Models.SubscriptionPolicy() { Batchable = false, UpdateFrequency = v2018_07_16.Models.UpdateFrequency.EveryDay },
            SourceRepository = $"{subscription1.SourceRepository}-updated",
            PullRequestFailureNotificationTags = anInvalidDependencyFlowNotificationList
        };

        {
            IActionResult result = await _data.SubscriptionsController.UpdateSubscription(createdSubscription1.Id, badUpdate);
            result.Should().BeAssignableTo<BadRequestObjectResult>();
        }
    }

    private class MockOrg : Octokit.Organization
    {
        public MockOrg(int id, string login)
        {
            Id = id;
            Login = login;
        }
    }

    [TestDependencyInjectionSetup]
    private static class TestDataConfiguration
    {
        public static void Dependencies(IServiceCollection collection)
        {
            var mockWorkItemProducerFactory = new Mock<IWorkItemProducerFactory>();
            var mockSubscriptionTriggerWorkItemProducer = new Mock<IWorkItemProducer<SubscriptionTriggerWorkItem>>();
            var mockBuildCoherencyInfoWorkItem = new Mock<IWorkItemProducer<BuildCoherencyInfoWorkItem>>();

            mockWorkItemProducerFactory
                .Setup(f => f.CreateProducer<SubscriptionTriggerWorkItem>(false))
                .Returns(mockSubscriptionTriggerWorkItemProducer.Object);

            mockWorkItemProducerFactory
                .Setup(f => f.CreateProducer<BuildCoherencyInfoWorkItem>(false))
                .Returns(mockBuildCoherencyInfoWorkItem.Object);

            mockWorkItemProducerFactory
                .Setup(f => f.CreateProducer<SubscriptionTriggerWorkItem>(true))
                .Returns(mockSubscriptionTriggerWorkItemProducer.Object);
        
            collection.AddLogging(l => l.AddProvider(new NUnitLogger()));
            collection.AddSingleton<IHostEnvironment>(new HostingEnvironment
            {
                EnvironmentName = Environments.Development
            });
            collection.AddSingleton(Mock.Of<IRemoteFactory>());
            collection.AddSingleton(Mock.Of<IBasicBarClient>());
            collection.AddSingleton(mockWorkItemProducerFactory.Object);
            collection.AddSingleton(_mockInstallationIdResolver.Object);
        }

        public static void GitHub(IServiceCollection collection)
        {
            var gitHubClient = new Mock<Octokit.IGitHubClient>(MockBehavior.Strict);

            gitHubClient.Setup(ghc => ghc.Organization.GetAllForUser(It.IsAny<string>()))
                .Returns((string userLogin) => CallFakeGetAllForUser(userLogin));

            var clientFactoryMock = new Mock<IGitHubClientFactory>();
            clientFactoryMock.Setup(f => f.CreateGitHubClient(It.IsAny<string>()))
                .Returns((string token) => gitHubClient.Object);
            collection.AddSingleton(clientFactoryMock.Object);
            collection.Configure<GitHubClientOptions>(o =>
            {
                o.ProductHeader = new Octokit.ProductHeaderValue("TEST", "1.0");
            });

            static async Task<IReadOnlyList<Octokit.Organization>> CallFakeGetAllForUser(string userLogin)
            {
                await Task.Delay(0); // Added just to suppress green squiggles
                List<Octokit.Organization> returnValue = [];

                switch (userLogin.ToLower())
                {
                    case "somemicrosoftuser": // valid user, in MS org
                        returnValue.Add(MockOrganization(123, "microsoft"));
                        break;
                    case "someexternaluser":  // "real" user, but not in MS org
                        returnValue.Add(MockOrganization(456, "definitely-not-microsoft"));
                        break;
                    default: // Any other user; GitHub "teams" will fall through here.
                        throw new Octokit.NotFoundException("Unknown user", HttpStatusCode.NotFound);
                }

                return returnValue.AsReadOnly();
            }
        }


        public static async Task<Func<IServiceProvider, Task>> DataContext(IServiceCollection collection)
        {
            var connectionString = await SharedData.Database.GetConnectionString();
            collection.AddBuildAssetRegistry(options =>
            {
                options.UseSqlServer(connectionString);
                options.EnableServiceProviderCaching(false);
            });

            return async provider =>
            {
                var testChannelName = "test-channel-sub-controller20200220";
                var defaultGitHubSourceRepo = "https://github.com/dotnet/sub-controller-test-source-repo";
                var defaultGitHubTargetRepo = "https://github.com/dotnet/sub-controller-test-target-repo";
                var defaultAzdoSourceRepo =
                    "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-source-repo";
                var defaultAzdoTargetRepo =
                    "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-target-repo";
                var deleteScenarioSourceRepo = "https://github.com/dotnet/sub-controller-delete-sub-source-repo";
                var deleteScenarioTargetRepo = "https://github.com/dotnet/sub-controller-delete-sub-target-repo";
                var triggerScenarioSourceRepo =
                    "https://github.com/dotnet/sub-controller-trigger-sub-source-repo";
                var triggerScenarioTargetRepo =
                    "https://github.com/dotnet/sub-controller-trigger-sub-target-repo";
                var defaultClassification = "classy-classification";
                uint defaultInstallationId = 1234;

                // Setup common data context stuff for the background
                var dataContext = provider.GetRequiredService<BuildAssetRegistryContext>();

                await dataContext.Channels.AddAsync(
                    new Maestro.Data.Models.Channel()
                    {
                        Name = testChannelName,
                        Classification = defaultClassification
                    }
                );

                // Add some repos
                await dataContext.Repositories.AddAsync(
                    new Maestro.Data.Models.Repository()
                    {
                        RepositoryName = defaultGitHubSourceRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Maestro.Data.Models.Repository()
                    {
                        RepositoryName = defaultGitHubTargetRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Maestro.Data.Models.Repository()
                    {
                        RepositoryName = defaultAzdoSourceRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Maestro.Data.Models.Repository()
                    {
                        RepositoryName = defaultAzdoTargetRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Maestro.Data.Models.Repository()
                    {
                        RepositoryName = deleteScenarioSourceRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Maestro.Data.Models.Repository()
                    {
                        RepositoryName = deleteScenarioTargetRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Maestro.Data.Models.Repository()
                    {
                        RepositoryName = triggerScenarioSourceRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Maestro.Data.Models.Repository()
                    {
                        RepositoryName = triggerScenarioTargetRepo,
                        InstallationId = defaultInstallationId
                    }
                );

                await dataContext.SaveChangesAsync();
            };
        }

        public static Func<IServiceProvider, ChannelsController> ChannelsController(IServiceCollection collection)
        {
            collection.AddSingleton<ChannelsController>();
            return s => s.GetRequiredService<ChannelsController>();
        }

        public static Func<IServiceProvider, SubscriptionsController> SubscriptionsController(IServiceCollection collection)
        {
            collection.AddSingleton<SubscriptionsController>();
            return s => s.GetRequiredService<SubscriptionsController>();
        }

        public static Func<IServiceProvider, BuildsController> BuildsController(IServiceCollection collection)
        {
            collection.AddSingleton<BuildsController>();
            return s => s.GetRequiredService<BuildsController>();
        }

        public static Func<IServiceProvider, TestClock> Clock(IServiceCollection collection)
        {
            collection.AddSingleton<ISystemClock, TestClock>();
            return s => (TestClock)s.GetRequiredService<ISystemClock>();
        }
    }

    // Copied from GitHubClaimsResolverTests; could refactor if needed in another place
    private static MockOrg MockOrganization(int id, string login)
    {
        return new MockOrg(id, login);
    }
}

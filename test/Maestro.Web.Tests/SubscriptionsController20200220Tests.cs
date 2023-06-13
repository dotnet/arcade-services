// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Common;
using Maestro.Data;
using Maestro.Web.Api.v2020_02_20.Controllers;
using Maestro.Web.Api.v2020_02_20.Models;
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
using NUnit.Framework;

namespace Maestro.Web.Tests;

[TestFixture]
public partial class SubscriptionsController20200220Tests : IDisposable
{
    private readonly TestData data;

    public SubscriptionsController20200220Tests()
    {
        data = TestData.Default.Build();
    }

    public void Dispose()
    {
        data.Dispose();
    }

    [Test]
    public async Task CreateGetAndListSubscriptions()
    {
        string testChannelName = "test-channel-sub-controller20200220";
        string defaultGitHubSourceRepo = "https://github.com/dotnet/sub-controller-test-source-repo";
        string defaultGitHubTargetRepo = "https://github.com/dotnet/sub-controller-test-target-repo";
        string defaultAzdoSourceRepo = "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-source-repo";
        string defaultAzdoTargetRepo = "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-target-repo";
        string defaultBranchName = "main";
        string aValidDependencyFlowNotificationList = "@someMicrosoftUser;@some-github-team";


        // Create two subscriptions
        SubscriptionData subscription1 = new SubscriptionData()
        {
            ChannelName = testChannelName,
            Enabled = true,
            SourceRepository = defaultGitHubSourceRepo,
            TargetRepository = defaultGitHubTargetRepo,
            Policy = new Api.v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = Api.v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = defaultBranchName,
            PullRequestFailureNotificationTags = aValidDependencyFlowNotificationList
        };

        Subscription createdSubscription1;
        {
            IActionResult result = await data.SubscriptionsController.Create(subscription1);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult) result;
            objResult.StatusCode.Should().Be((int) HttpStatusCode.Created);
            objResult.Value.Should().BeAssignableTo<Subscription>();
            createdSubscription1 = (Subscription) objResult.Value;
            createdSubscription1.Channel.Name.Should().Be(testChannelName);
            createdSubscription1.Policy.Batchable.Should().Be(true);
            createdSubscription1.Policy.UpdateFrequency.Should().Be(Api.v2018_07_16.Models.UpdateFrequency.EveryWeek);
            createdSubscription1.TargetBranch.Should().Be(defaultBranchName);
            createdSubscription1.SourceRepository.Should().Be(defaultGitHubSourceRepo);
            createdSubscription1.TargetRepository.Should().Be(defaultGitHubTargetRepo);
            createdSubscription1.PullRequestFailureNotificationTags.Should().Be(aValidDependencyFlowNotificationList);
        }

        SubscriptionData subscription2 = new SubscriptionData()
        {
            ChannelName = testChannelName,
            Enabled = false,
            SourceRepository = defaultAzdoSourceRepo,
            TargetRepository = defaultAzdoTargetRepo,
            Policy = new Api.v2018_07_16.Models.SubscriptionPolicy() { Batchable = false, UpdateFrequency = Api.v2018_07_16.Models.UpdateFrequency.None },
            TargetBranch = defaultBranchName
        };

        Subscription createdSubscription2;
        {
            IActionResult result = await data.SubscriptionsController.Create(subscription2);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult) result;
            objResult.StatusCode.Should().Be((int) HttpStatusCode.Created);
            objResult.Value.Should().BeAssignableTo<Subscription>();
            createdSubscription2 = (Subscription) objResult.Value;
            createdSubscription2.Channel.Name.Should().Be(testChannelName);
            createdSubscription2.Policy.Batchable.Should().Be(false);
            createdSubscription2.Policy.UpdateFrequency.Should().Be(Api.v2018_07_16.Models.UpdateFrequency.None);
            createdSubscription2.TargetBranch.Should().Be(defaultBranchName);
            createdSubscription2.SourceRepository.Should().Be(defaultAzdoSourceRepo);
            createdSubscription2.TargetRepository.Should().Be(defaultAzdoTargetRepo);
            createdSubscription2.PullRequestFailureNotificationTags.Should().BeNull();
        }

        // List all (both) subscriptions, spot check that we got both
        {
            IActionResult result = data.SubscriptionsController.ListSubscriptions();
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult) result;
            objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
            List<Subscription> listedSubs = ((IEnumerable<Subscription>) objResult.Value).ToList();
            listedSubs.Count.Should().Be(2);
            listedSubs[0].Enabled.Should().Be(true);
            listedSubs[0].TargetRepository.Should().Be(defaultGitHubTargetRepo);
            listedSubs[0].PullRequestFailureNotificationTags.Should().Be(aValidDependencyFlowNotificationList);
            listedSubs[1].Enabled.Should().Be(false);
            listedSubs[1].TargetRepository.Should().Be(defaultAzdoTargetRepo);
            listedSubs[1].PullRequestFailureNotificationTags.Should().BeNull();
        }
        // Use ListSubscriptions() params at least superficially to go down those codepaths
        {
            IActionResult result = data.SubscriptionsController.ListSubscriptions(defaultAzdoSourceRepo, defaultAzdoTargetRepo, createdSubscription2.Channel.Id, false);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult) result;
            objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
            List<Subscription> listedSubs = ((IEnumerable<Subscription>) objResult.Value).ToList();
            listedSubs.Count.Should().Be(1);
            listedSubs[0].Enabled.Should().Be(false);
            listedSubs[0].TargetRepository.Should().Be(defaultAzdoTargetRepo);
            listedSubs[0].PullRequestFailureNotificationTags.Should().BeNull(); // This is sub2
        }
        // Directly get one of the subscriptions
        {
            IActionResult result = await data.SubscriptionsController.GetSubscription(createdSubscription1.Id);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult) result;
            objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
            Subscription theSubscription = (Subscription) objResult.Value;
            theSubscription.Enabled.Should().Be(true);
            theSubscription.TargetRepository.Should().Be(defaultGitHubTargetRepo);
            theSubscription.PullRequestFailureNotificationTags.Should().Be(aValidDependencyFlowNotificationList);
        }
    }

    [Test]
    public async Task GetAndListNonexistentSubscriptions()
    {
        Guid shouldntExist = Guid.Parse("00000000-0000-0000-0000-000000000042");

        // No subs added, get a random Guid
        {
            IActionResult result = await data.SubscriptionsController.GetSubscription(shouldntExist);
            result.Should().BeAssignableTo<NotFoundResult>();
            var notFoundResult = (NotFoundResult) result;
            notFoundResult.StatusCode.Should().Be((int) HttpStatusCode.NotFound);
        }

        {
            IActionResult result = data.SubscriptionsController.ListSubscriptions(
                "https://github.com/dotnet/does-not-exist",
                "https://github.com/dotnet/does-not-exist-2",
                123456,
                true);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult) result;
            objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
            List<Subscription> listedSubs = ((IEnumerable<Subscription>) objResult.Value).ToList();
            listedSubs.Should().BeEmpty();
        }
    }

    [Test]
    public async Task CreateSubscriptionForNonMicrosoftUserFails()
    {
        string testChannelName = "test-channel-sub-controller20200220";
        string defaultGitHubSourceRepo = "https://github.com/dotnet/sub-controller-test-source-repo";
        string defaultGitHubTargetRepo = "https://github.com/dotnet/sub-controller-test-target-repo";
        string defaultBranchName = "main";
        string anInvalidDependencyFlowNotificationList = "@someexternaluser;@somemicrosoftuser;@some-team";

        // @someexternaluser will resolve as not in the microsoft org and should fail
        SubscriptionData subscription = new SubscriptionData()
        {
            ChannelName = testChannelName,
            Enabled = true,
            SourceRepository = defaultGitHubSourceRepo,
            TargetRepository = defaultGitHubTargetRepo,
            Policy = new Api.v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = Api.v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = defaultBranchName,
            PullRequestFailureNotificationTags = anInvalidDependencyFlowNotificationList
        };

        IActionResult result = await data.SubscriptionsController.Create(subscription);
        result.Should().BeAssignableTo<BadRequestObjectResult>();
    }

    [Test]
    public async Task CreateSubscriptionForNonExistentChannelFails()
    {
        string defaultGitHubSourceRepo = "https://github.com/dotnet/sub-controller-test-source-repo";
        string defaultGitHubTargetRepo = "https://github.com/dotnet/sub-controller-test-target-repo";
        string defaultBranchName = "main";

        // Create two subscriptions
        SubscriptionData subscription = new SubscriptionData()
        {
            ChannelName = "this-channel-does-not-exist",
            Enabled = true,
            SourceRepository = defaultGitHubSourceRepo,
            TargetRepository = defaultGitHubTargetRepo,
            Policy = new Api.v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = Api.v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = defaultBranchName
        };

        IActionResult result = await data.SubscriptionsController.Create(subscription);
        result.Should().BeAssignableTo<BadRequestObjectResult>();
    }

    [Test]
    public async Task DeleteSubscription()
    {
        string testChannelName = "test-channel-sub-controller20200220";
        string deleteScenarioSourceRepo = "https://github.com/dotnet/sub-controller-delete-sub-source-repo";
        string deleteScenarioTargetRepo = "https://github.com/dotnet/sub-controller-delete-sub-target-repo";
        string defaultBranchName = "main";

        // Create two subscriptions
        SubscriptionData subscriptionToDelete = new SubscriptionData()
        {
            ChannelName = testChannelName,
            Enabled = true,
            SourceRepository = deleteScenarioSourceRepo,
            TargetRepository = deleteScenarioTargetRepo,
            Policy = new Api.v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = Api.v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = defaultBranchName
        };

        {
            IActionResult createResult = await data.SubscriptionsController.Create(subscriptionToDelete);
            createResult.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult) createResult;
            objResult.StatusCode.Should().Be((int) HttpStatusCode.Created);
            Subscription createdSubscription = (Subscription) objResult.Value;

            IActionResult deleteResult = await data.SubscriptionsController.DeleteSubscription(createdSubscription.Id);
            deleteResult.Should().BeAssignableTo<OkObjectResult>();
            var deleteObjResult = (OkObjectResult) deleteResult;
            // Seems like this should be OK but it gives created... 
            deleteObjResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task TriggerSubscription()
    {
        string testChannelName = "test-channel-sub-controller20200220";
        string triggerScenarioSourceRepo = "https://github.com/dotnet/sub-controller-trigger-sub-source-repo";
        string triggerScenarioTargetRepo = "https://github.com/dotnet/sub-controller-trigger-sub-target-repo";
        string defaultBranchName = "main";

        // Create two subscriptions
        SubscriptionData subscriptionToTrigger = new SubscriptionData()
        {
            ChannelName = testChannelName,
            Enabled = true,
            SourceRepository = triggerScenarioSourceRepo,
            TargetRepository = triggerScenarioTargetRepo,
            Policy = new Api.v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = Api.v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = defaultBranchName
        };

        Subscription createdSubscription;
        {
            IActionResult createResult = await data.SubscriptionsController.Create(subscriptionToTrigger);
            createResult.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult) createResult;
            objResult.StatusCode.Should().Be((int) HttpStatusCode.Created);
            createdSubscription = (Subscription) objResult.Value;
        }

        BuildData build1Data = new BuildData()
        {
            GitHubRepository = triggerScenarioSourceRepo,
            AzureDevOpsBuildId = 123
        };
        BuildData build2Data = new BuildData()
        {
            GitHubRepository = triggerScenarioSourceRepo,
            AzureDevOpsBuildId = 124
        };
        BuildData build3Data = new BuildData()
        {
            GitHubRepository = $"{triggerScenarioSourceRepo}-different",
            AzureDevOpsBuildId = 125
        };
        Build build1, build3;
        // Add some builds
        {
            IActionResult createResult1 = await data.BuildsController.Create(build1Data);
            createResult1.Should().BeAssignableTo<ObjectResult>();
            var objResult1 = (ObjectResult) createResult1;
            objResult1.StatusCode.Should().Be((int) HttpStatusCode.Created);
            build1 = (Build) objResult1.Value;

            // Ignored build, just obviates the previous one.
            IActionResult createResult2 = await data.BuildsController.Create(build2Data);
            createResult2.Should().BeAssignableTo<ObjectResult>();
            var objResult2 = (ObjectResult) createResult2;
            objResult2.StatusCode.Should().Be((int) HttpStatusCode.Created);

            IActionResult createResult3 = await data.BuildsController.Create(build3Data);
            createResult3.Should().BeAssignableTo<ObjectResult>();
            var objResult3 = (ObjectResult) createResult3;
            objResult3.StatusCode.Should().Be((int) HttpStatusCode.Created);
            build3 = (Build) objResult3.Value;
        }

        // Default scenario; 'trigger a subscription with latest build' codepath.
        {
            IActionResult triggerResult = await data.SubscriptionsController.TriggerSubscription(createdSubscription.Id);
            triggerResult.Should().BeAssignableTo<AcceptedResult>();
            var latestTriggerResult = (AcceptedResult) triggerResult;
            latestTriggerResult.StatusCode.Should().Be((int) HttpStatusCode.Accepted);
        }

        // Scenario2: 'trigger a subscription with specific build' codepath.
        {
            IActionResult triggerResult = await data.SubscriptionsController.TriggerSubscription(createdSubscription.Id, build1.Id);
            triggerResult.Should().BeAssignableTo<AcceptedResult>();
            var latestTriggerResult = (AcceptedResult) triggerResult;
            latestTriggerResult.StatusCode.Should().Be((int) HttpStatusCode.Accepted);
        }

        // Failure: Trigger a subscription with non-existent build id.
        {
            IActionResult triggerResult = await data.SubscriptionsController.TriggerSubscription(createdSubscription.Id, 123456);
            triggerResult.Should().BeAssignableTo<BadRequestObjectResult>();
            var latestTriggerResult = (BadRequestObjectResult) triggerResult;
            latestTriggerResult.StatusCode.Should().Be((int) HttpStatusCode.BadRequest);
        }

        // Failure: Trigger a subscription with non-existent build codepath.
        {
            IActionResult triggerResult = await data.SubscriptionsController.TriggerSubscription(createdSubscription.Id, build3.Id);
            triggerResult.Should().BeAssignableTo<BadRequestObjectResult>();
            var latestTriggerResult = (BadRequestObjectResult) triggerResult;
            latestTriggerResult.StatusCode.Should().Be((int) HttpStatusCode.BadRequest);
        }
    }

    [Test]
    public async Task UpdateSubscription()
    {
        string testChannelName = "test-channel-sub-controller20200220";
        string defaultGitHubSourceRepo = "https://github.com/dotnet/sub-controller-test-source-repo";
        string defaultGitHubTargetRepo = "https://github.com/dotnet/sub-controller-test-target-repo";
        string defaultBranchName = "main";
        string aValidDependencyFlowNotificationList = "@someMicrosoftUser;@some-github-team";
        string anInvalidDependencyFlowNotificationList = "@someExternalUser;@someMicrosoftUser;@some-team";

        // Create two subscriptions
        SubscriptionData subscription1 = new SubscriptionData()
        {
            ChannelName = testChannelName,
            Enabled = true,
            SourceRepository = $"{defaultGitHubSourceRepo}-needsupdate",
            TargetRepository = defaultGitHubTargetRepo,
            Policy = new Api.v2018_07_16.Models.SubscriptionPolicy() { Batchable = true, UpdateFrequency = Api.v2018_07_16.Models.UpdateFrequency.EveryWeek },
            TargetBranch = defaultBranchName
        };

        Subscription createdSubscription1;
        {
            IActionResult result = await data.SubscriptionsController.Create(subscription1);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult) result;
            objResult.StatusCode.Should().Be((int) HttpStatusCode.Created);
            objResult.Value.Should().BeAssignableTo<Subscription>();
            createdSubscription1 = (Subscription) objResult.Value;
            createdSubscription1.Channel.Name.Should().Be(testChannelName);
            createdSubscription1.Policy.Batchable.Should().Be(true);
            createdSubscription1.Policy.UpdateFrequency.Should().Be(Api.v2018_07_16.Models.UpdateFrequency.EveryWeek);
            createdSubscription1.TargetBranch.Should().Be(defaultBranchName);
            createdSubscription1.SourceRepository.Should().Be($"{defaultGitHubSourceRepo}-needsupdate");
            createdSubscription1.TargetRepository.Should().Be(defaultGitHubTargetRepo);
        }

        SubscriptionUpdate update = new SubscriptionUpdate()
        {
            Enabled = !subscription1.Enabled,
            Policy = new Api.v2018_07_16.Models.SubscriptionPolicy() { Batchable = false, UpdateFrequency = Api.v2018_07_16.Models.UpdateFrequency.EveryDay },
            SourceRepository = $"{subscription1.SourceRepository}-updated",
            PullRequestFailureNotificationTags = aValidDependencyFlowNotificationList
        };

        {
            IActionResult result = await data.SubscriptionsController.UpdateSubscription(createdSubscription1.Id, update);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult) result;
            objResult.StatusCode.Should().Be((int) HttpStatusCode.OK);
            objResult.Value.Should().BeAssignableTo<Subscription>();
            // Could also do a get after this; that more tests the underlying data context though.
            var updatedSubscription = (Subscription) objResult.Value;
            updatedSubscription.Id.Should().Be(createdSubscription1.Id);
            updatedSubscription.Enabled.Should().Be(!subscription1.Enabled.Value);
            updatedSubscription.Policy.UpdateFrequency.Should().Be(Api.v2018_07_16.Models.UpdateFrequency.EveryDay);
            updatedSubscription.SourceRepository.Should().Be($"{subscription1.SourceRepository}-updated");
            updatedSubscription.PullRequestFailureNotificationTags.Should().Be(aValidDependencyFlowNotificationList);
        }

        // Update with an invalid list, make sure it fails
        SubscriptionUpdate badUpdate = new SubscriptionUpdate()
        {
            Enabled = !subscription1.Enabled,
            Policy = new Api.v2018_07_16.Models.SubscriptionPolicy() { Batchable = false, UpdateFrequency = Api.v2018_07_16.Models.UpdateFrequency.EveryDay },
            SourceRepository = $"{subscription1.SourceRepository}-updated",
            PullRequestFailureNotificationTags = anInvalidDependencyFlowNotificationList
        };

        {
            IActionResult result = await data.SubscriptionsController.UpdateSubscription(createdSubscription1.Id, badUpdate);
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
            collection.AddLogging(l => l.AddProvider(new NUnitLogger()));
            collection.AddSingleton<IHostEnvironment>(new HostingEnvironment
            {
                EnvironmentName = Environments.Development
            });
            collection.AddSingleton(Mock.Of<IRemoteFactory>());
            collection.AddSingleton<IBackgroundQueue, NeverBackgroundQueue>();
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
                List<Octokit.Organization> returnValue = new List<Octokit.Organization>();

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
            string connectionString = await SharedData.Database.GetConnectionString();
            collection.AddBuildAssetRegistry(options =>
            {
                options.UseSqlServer(connectionString);
                options.EnableServiceProviderCaching(false);
            });

            return async provider =>
            {
                string testChannelName = "test-channel-sub-controller20200220";
                string defaultGitHubSourceRepo = "https://github.com/dotnet/sub-controller-test-source-repo";
                string defaultGitHubTargetRepo = "https://github.com/dotnet/sub-controller-test-target-repo";
                string defaultAzdoSourceRepo =
                    "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-source-repo";
                string defaultAzdoTargetRepo =
                    "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-target-repo";
                string deleteScenarioSourceRepo = "https://github.com/dotnet/sub-controller-delete-sub-source-repo";
                string deleteScenarioTargetRepo = "https://github.com/dotnet/sub-controller-delete-sub-target-repo";
                string triggerScenarioSourceRepo =
                    "https://github.com/dotnet/sub-controller-trigger-sub-source-repo";
                string triggerScenarioTargetRepo =
                    "https://github.com/dotnet/sub-controller-trigger-sub-target-repo";
                string defaultClassification = "classy-classification";
                uint defaultInstallationId = 1234;

                // Setup common data context stuff for the background
                var dataContext = provider.GetRequiredService<BuildAssetRegistryContext>();

                await dataContext.Channels.AddAsync(
                    new Data.Models.Channel()
                    {
                        Name = testChannelName,
                        Classification = defaultClassification
                    }
                );

                // Add some repos
                await dataContext.Repositories.AddAsync(
                    new Data.Models.Repository()
                    {
                        RepositoryName = defaultGitHubSourceRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Data.Models.Repository()
                    {
                        RepositoryName = defaultGitHubTargetRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Data.Models.Repository()
                    {
                        RepositoryName = defaultAzdoSourceRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Data.Models.Repository()
                    {
                        RepositoryName = defaultAzdoTargetRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Data.Models.Repository()
                    {
                        RepositoryName = deleteScenarioSourceRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Data.Models.Repository()
                    {
                        RepositoryName = deleteScenarioTargetRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Data.Models.Repository()
                    {
                        RepositoryName = triggerScenarioSourceRepo,
                        InstallationId = defaultInstallationId
                    }
                );
                await dataContext.Repositories.AddAsync(
                    new Data.Models.Repository()
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
            return s => (TestClock) s.GetRequiredService<ISystemClock>();
        }
    }

    // Copied from GitHubClaimsResolverTests; could refactor if needed in another place
    private static Octokit.Organization MockOrganization(int id, string login)
    {
        return new MockOrg(id, login);
    }
}

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
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Maestro.Web.Tests
{
    [TestFixture]
    public class SubscriptionsController20200220Tests
    {
        const string testChannelName = "test-channel-sub-controller20200220";
        const string defaultGitHubSourceRepo = "https://github.com/dotnet/sub-controller-test-source-repo";
        const string defaultGitHubTargetRepo = "https://github.com/dotnet/sub-controller-test-target-repo";
        const string defaultAzdoSourceRepo = "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-source-repo";
        const string defaultAzdoTargetRepo = "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-target-repo";
        const string deleteScenarioSourceRepo = "https://github.com/dotnet/sub-controller-delete-sub-source-repo";
        const string deleteScenarioTargetRepo = "https://github.com/dotnet/sub-controller-delete-sub-target-repo";
        const string triggerScenarioSourceRepo = "https://github.com/dotnet/sub-controller-trigger-sub-source-repo";
        const string triggerScenarioTargetRepo = "https://github.com/dotnet/sub-controller-trigger-sub-target-repo";
        const string defaultBranchName = "main";
        const string defaultClassification = "classy-classification";
        const uint defaultInstallationId = 1234;
        TestData data;

        public SubscriptionsController20200220Tests()
        {
            data = GetTestData();
        }

        [Test]
        public async Task CreateGetAndListSubscriptions()
        {
            // Create two subscriptions
            Api.v2018_07_16.Models.SubscriptionData subscription1 = new Api.v2018_07_16.Models.SubscriptionData()
            {
                ChannelName = testChannelName,
                Enabled = true,
                SourceRepository = defaultGitHubSourceRepo,
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
                createdSubscription1.SourceRepository.Should().Be(defaultGitHubSourceRepo);
                createdSubscription1.TargetRepository.Should().Be(defaultGitHubTargetRepo);
            }

            Api.v2018_07_16.Models.SubscriptionData subscription2 = new Api.v2018_07_16.Models.SubscriptionData()
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
                listedSubs[1].Enabled.Should().Be(false);
                listedSubs[1].TargetRepository.Should().Be(defaultAzdoTargetRepo);
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
            }
        }

        [Test]
        public async Task GetAndListNonexistentSubscriptions()
        {
            // No subs added, get a random Guid
            {
                IActionResult result = await data.SubscriptionsController.GetSubscription(Guid.NewGuid());
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
        public async Task CreateSubscriptionForNonExistentChannelFails()
        {
            // Create two subscriptions
            Api.v2018_07_16.Models.SubscriptionData subscription = new Api.v2018_07_16.Models.SubscriptionData()
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
            var objResult = (BadRequestObjectResult) result;
            objResult.StatusCode.Should().Be((int) HttpStatusCode.BadRequest);
        }

        [Test]
        public async Task DeleteSubscription()
        {
            // Create two subscriptions
            Api.v2018_07_16.Models.SubscriptionData subscriptionToDelete = new Api.v2018_07_16.Models.SubscriptionData()
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
            // Create two subscriptions
            Api.v2018_07_16.Models.SubscriptionData subscriptionToTrigger = new Api.v2018_07_16.Models.SubscriptionData()
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

            // Failure: Trigger a subscription with non-existent build codepath.
            {
                IActionResult triggerResult = await data.SubscriptionsController.TriggerSubscription(createdSubscription.Id, 123456);
                triggerResult.Should().BeAssignableTo<NotFoundResult>();
                var latestTriggerResult = (NotFoundResult) triggerResult;
                latestTriggerResult.StatusCode.Should().Be((int) HttpStatusCode.NotFound);
            }

            // Failure: Trigger a subscription with non-existent build codepath.
            {
                IActionResult triggerResult = await data.SubscriptionsController.TriggerSubscription(createdSubscription.Id, build3.Id);
                triggerResult.Should().BeAssignableTo<NotFoundResult>();
                var latestTriggerResult = (NotFoundResult) triggerResult;
                latestTriggerResult.StatusCode.Should().Be((int) HttpStatusCode.NotFound);
            }
        }

        [Test]
        public async Task UpdateSubscription()
        {
            // Create two subscriptions
            Api.v2018_07_16.Models.SubscriptionData subscription1 = new Api.v2018_07_16.Models.SubscriptionData()
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

            Api.v2018_07_16.Models.SubscriptionUpdate update = new Api.v2018_07_16.Models.SubscriptionUpdate()
            {
                Enabled = !subscription1.Enabled,
                Policy = new Api.v2018_07_16.Models.SubscriptionPolicy() { Batchable = false, UpdateFrequency = Api.v2018_07_16.Models.UpdateFrequency.EveryDay },
                SourceRepository = $"{subscription1.SourceRepository}-updated"
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
                updatedSubscription.Enabled.Should().IsSameOrEqualTo(!subscription1.Enabled);
                updatedSubscription.Policy.UpdateFrequency.Should().Be(Api.v2018_07_16.Models.UpdateFrequency.EveryDay);
                updatedSubscription.SourceRepository.Should().Be($"{subscription1.SourceRepository}-updated");
            }
        }

        private TestData GetTestData()
        {
            return BuildDefaultAsync().GetAwaiter().GetResult();
        }

        private Task<TestData> BuildDefaultAsync()
        {
            return new TestDataBuilder().BuildAsync();
        }

        private sealed class TestDataBuilder
        {
            private Type _backgroundQueueType = typeof(NeverBackgroundQueue);

            public TestDataBuilder WithImmediateBackgroundQueue()
            {
                _backgroundQueueType = typeof(ImmediateBackgroundQueue);
                return this;
            }

            public async Task<TestData> BuildAsync()
            {
                string connectionString = await SharedData.Database.GetConnectionString();

                var collection = new ServiceCollection();
                collection.AddLogging(l => l.AddProvider(new NUnitLogger()));
                collection.AddSingleton<IHostEnvironment>(new HostingEnvironment
                {
                    EnvironmentName = Environments.Development
                });
                collection.AddBuildAssetRegistry(options =>
                {
                    options.UseSqlServer(connectionString);
                    options.EnableServiceProviderCaching(false);
                });
                collection.AddSingleton<SubscriptionsController>();
                collection.AddSingleton<ChannelsController>();
                collection.AddSingleton<BuildsController>();
                collection.AddSingleton<ISystemClock, TestClock>();
                collection.AddSingleton(Mock.Of<IRemoteFactory>());
                collection.AddSingleton(typeof(IBackgroundQueue), _backgroundQueueType);
                ServiceProvider provider = collection.BuildServiceProvider();

                // Setup common data context stuff for the background
                var dataContext = provider.GetRequiredService<BuildAssetRegistryContext>();

                await dataContext.Channels.AddAsync(new Data.Models.Channel()
                {
                    Name = testChannelName,
                    Classification = defaultClassification
                });

                // Add some repos
                await dataContext.Repositories.AddAsync(new Data.Models.Repository()
                {
                    RepositoryName = defaultGitHubSourceRepo,
                    InstallationId = defaultInstallationId
                });
                await dataContext.Repositories.AddAsync(new Data.Models.Repository()
                {
                    RepositoryName = defaultGitHubTargetRepo,
                    InstallationId = defaultInstallationId
                });
                await dataContext.Repositories.AddAsync(new Data.Models.Repository()
                {
                    RepositoryName = defaultAzdoSourceRepo,
                    InstallationId = defaultInstallationId
                });
                await dataContext.Repositories.AddAsync(new Data.Models.Repository()
                {
                    RepositoryName = defaultAzdoTargetRepo,
                    InstallationId = defaultInstallationId
                });
                await dataContext.Repositories.AddAsync(new Data.Models.Repository()
                {
                    RepositoryName = deleteScenarioSourceRepo,
                    InstallationId = defaultInstallationId
                });
                await dataContext.Repositories.AddAsync(new Data.Models.Repository()
                {
                    RepositoryName = deleteScenarioTargetRepo,
                    InstallationId = defaultInstallationId
                });
                await dataContext.Repositories.AddAsync(new Data.Models.Repository()
                {
                    RepositoryName = triggerScenarioSourceRepo,
                    InstallationId = defaultInstallationId
                });
                await dataContext.Repositories.AddAsync(new Data.Models.Repository()
                {
                    RepositoryName = triggerScenarioTargetRepo,
                    InstallationId = defaultInstallationId
                });

                await dataContext.SaveChangesAsync();

                var clock = (TestClock) provider.GetRequiredService<ISystemClock>();

                return new TestData(provider, clock);
            }
        }

        private sealed class TestData : IDisposable
        {
            private readonly ServiceProvider _provider;
            public TestClock Clock { get; }

            public TestData(ServiceProvider provider, TestClock clock)
            {
                _provider = provider;
                Clock = clock;
            }

            public ChannelsController ChannelsController => _provider.GetRequiredService<ChannelsController>();
            public SubscriptionsController SubscriptionsController => _provider.GetRequiredService<SubscriptionsController>();
            public BuildsController BuildsController => _provider.GetRequiredService<BuildsController>();

            public void Dispose()
            {
                _provider.Dispose();
            }
        }
    }
}

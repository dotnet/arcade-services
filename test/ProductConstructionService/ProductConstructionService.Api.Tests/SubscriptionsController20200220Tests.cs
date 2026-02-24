// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using AwesomeAssertions;
using Azure.ResourceManager.Resources;
using Maestro.Common.Cache;
using Maestro.Data;
using Maestro.DataProviders;
using Maestro.DataProviders.ConfigurationIngestion;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProductConstructionService.Api.Api;
using ProductConstructionService.Api.Api.v2020_02_20.Controllers;
using ProductConstructionService.Api.Controllers;
using ProductConstructionService.Api.v2020_02_20.Models;
using ProductConstructionService.Common;
using ProductConstructionService.DependencyFlow.Tests.Mocks;
using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

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
        var testChannelName = $"test-channel-{Guid.NewGuid()}";
        var defaultGitHubSourceRepo = "https://github.com/dotnet/sub-controller-test-source-repo";
        var defaultGitHubTargetRepo = "https://github.com/dotnet/sub-controller-test-target-repo";
        var defaultAzdoSourceRepo = "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-source-repo";
        var defaultAzdoTargetRepo = "https://dev.azure.com/dnceng/internal/_git/sub-controller-test-target-repo";
        var branchName1 = "a";
        var branchName2 = "b";
        var aValidDependencyFlowNotificationList = "@someMicrosoftUser;@some-github-team";
        var subscriptionId1 = Guid.NewGuid();
        var subscriptionId2 = Guid.NewGuid();

        var yamlConfiguration = new YamlConfiguration(
                Subscriptions: [
                    new SubscriptionYaml
                    {
                        Id = subscriptionId1,
                        Channel = testChannelName,
                        Enabled = true,
                        SourceRepository = defaultGitHubSourceRepo,
                        TargetRepository = defaultGitHubTargetRepo,
                        Batchable = true,
                        UpdateFrequency = Microsoft.DotNet.ProductConstructionService.Client.Models.UpdateFrequency.EveryWeek,
                        TargetBranch = branchName1,
                        FailureNotificationTags = aValidDependencyFlowNotificationList
                    },
                    new SubscriptionYaml
                    {
                        Id = subscriptionId2,
                        Channel = testChannelName,
                        Enabled = false,
                        SourceRepository = defaultAzdoSourceRepo,
                        TargetRepository = defaultAzdoTargetRepo,
                        Batchable = false,
                        UpdateFrequency = Microsoft.DotNet.ProductConstructionService.Client.Models.UpdateFrequency.None,
                        TargetBranch = branchName2,
                        SourceEnabled = true,
                        SourceDirectory = "sub-controller-test-source-repo",
                        ExcludedAssets = [DependencyFileManager.ArcadeSdkPackageName, "Foo.Bar"]
                    }
                ],
                Channels: [new ChannelYaml { Name = testChannelName, Classification = "test" }],
                DefaultChannels: [],
                BranchMergePolicies: []);

        SubscriptionYaml createdSubscription1, createdSubscription2;
        {
            var result = await _data.ConfigurationIngestionController.IngestNamespace(
                nameof(CreateGetAndListSubscriptions),
                yamlConfiguration);
            result.Should().BeAssignableTo<ObjectResult>();
            var objResult = (ObjectResult)result;
            objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
            objResult.Value.Should().BeAssignableTo<ConfigurationUpdates>();
            var configUpdates = (ConfigurationUpdates)objResult.Value!;

            configUpdates.Subscriptions.Creations.Should().HaveCount(2);
            configUpdates.Channels.Creations.Should().HaveCount(1);
            createdSubscription1 = configUpdates.Subscriptions.Creations.First(sub => sub.Id == subscriptionId1);
            createdSubscription2 = configUpdates.Subscriptions.Creations.First(sub => sub.Id == subscriptionId2);

            createdSubscription1.Channel.Should().Be(testChannelName);
            createdSubscription1.Batchable.Should().Be(true);
            createdSubscription1.UpdateFrequency.Should().Be(Microsoft.DotNet.ProductConstructionService.Client.Models.UpdateFrequency.EveryWeek);
            createdSubscription1.TargetBranch.Should().Be(branchName1);
            createdSubscription1.SourceRepository.Should().Be(defaultGitHubSourceRepo);
            createdSubscription1.TargetRepository.Should().Be(defaultGitHubTargetRepo);
            createdSubscription1.FailureNotificationTags.Should().Be(aValidDependencyFlowNotificationList);
            createdSubscription1.SourceEnabled.Should().Be(false);
            createdSubscription1.ExcludedAssets.Should().BeEmpty();

            createdSubscription2.Channel.Should().Be(testChannelName);
            createdSubscription2.Batchable.Should().Be(false);
            createdSubscription2.UpdateFrequency.Should().Be(Microsoft.DotNet.ProductConstructionService.Client.Models.UpdateFrequency.None);
            createdSubscription2.TargetBranch.Should().Be(branchName2);
            createdSubscription2.SourceRepository.Should().Be(defaultAzdoSourceRepo);
            createdSubscription2.TargetRepository.Should().Be(defaultAzdoTargetRepo);
            createdSubscription2.FailureNotificationTags.Should().BeNull();
            createdSubscription2.SourceEnabled.Should().Be(true);
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
            IActionResult result = _data.SubscriptionsController.ListSubscriptions(defaultAzdoSourceRepo, defaultAzdoTargetRepo, enabled: false, sourceEnabled: true);
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
    public async Task TriggerSubscription()
    {
        var testChannelName = $"test-channel-{Guid.NewGuid()}";
        var triggerScenarioSourceRepo = "https://github.com/dotnet/sub-controller-trigger-sub-source-repo";
        var triggerScenarioTargetRepo = "https://github.com/dotnet/sub-controller-trigger-sub-target-repo";
        var defaultBranchName = "main";
        var subscriptionId1 = Guid.NewGuid();

        var yamlConfiguration = new YamlConfiguration(
                Subscriptions: [
                    new SubscriptionYaml
                    {
                        Id = subscriptionId1,
                        Channel = testChannelName,
                        Enabled = true,
                        SourceRepository = triggerScenarioSourceRepo,
                        TargetRepository = triggerScenarioTargetRepo,
                        Batchable = true,
                        UpdateFrequency = Microsoft.DotNet.ProductConstructionService.Client.Models.UpdateFrequency.EveryWeek,
                        TargetBranch = defaultBranchName,
                    },
                ],
                Channels: [new ChannelYaml { Name = testChannelName, Classification = "test" }],
                DefaultChannels: [],
                BranchMergePolicies: []);

        var result = await _data.ConfigurationIngestionController.IngestNamespace(
                nameof(TriggerSubscription),
                yamlConfiguration);
        result.Should().BeAssignableTo<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);

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
            IActionResult triggerResult = await _data.SubscriptionsController.TriggerSubscription(subscriptionId1);
            triggerResult.Should().BeAssignableTo<AcceptedResult>();
            var latestTriggerResult = (AcceptedResult)triggerResult;
            latestTriggerResult.StatusCode.Should().Be((int)HttpStatusCode.Accepted);
        }

        // Scenario2: 'trigger a subscription with specific build' codepath.
        {
            IActionResult triggerResult = await _data.SubscriptionsController.TriggerSubscription(subscriptionId1, build1.Id);
            triggerResult.Should().BeAssignableTo<AcceptedResult>();
            var latestTriggerResult = (AcceptedResult)triggerResult;
            latestTriggerResult.StatusCode.Should().Be((int)HttpStatusCode.Accepted);
        }

        // Failure: Trigger a subscription with non-existent build id.
        {
            IActionResult triggerResult = await _data.SubscriptionsController.TriggerSubscription(subscriptionId1, 123456);
            triggerResult.Should().BeAssignableTo<BadRequestObjectResult>();
            var latestTriggerResult = (BadRequestObjectResult)triggerResult;
            latestTriggerResult.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        }

        // Failure: Trigger a subscription with non-existent build codepath.
        {
            IActionResult triggerResult = await _data.SubscriptionsController.TriggerSubscription(subscriptionId1, build3.Id);
            triggerResult.Should().BeAssignableTo<BadRequestObjectResult>();
            var latestTriggerResult = (BadRequestObjectResult)triggerResult;
            latestTriggerResult.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
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

            collection.AddSingleton<IOptions<EnvironmentNamespaceOptions>>(
                new OptionsWrapper<EnvironmentNamespaceOptions>(
                    new EnvironmentNamespaceOptions
                    {
                        DefaultNamespaceName = TestDatabase.TestNamespace
                    }));
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

        public static void AddConfigurationIngestor(IServiceCollection collection)
        {
            var installationIdResolver = new Mock<IGitHubInstallationIdResolver>();
            installationIdResolver.Setup(r => r.GetInstallationIdForRepository(It.IsAny<string>()))
                .Returns(Task.FromResult((long?)1));
            var ghTagValidator = new Mock<IGitHubTagValidator>();
            ghTagValidator.Setup(v => v.IsNotificationTagValidAsync(It.IsAny<string>()))
                .Returns(Task.FromResult(true));
            var kustoClientProvider = new Mock<IKustoClientProvider>();

            collection.AddSingleton(installationIdResolver.Object)
                .AddSingleton<IConfigurationIngestor, ConfigurationIngestor>()
                .AddSingleton(kustoClientProvider.Object)
                .AddSingleton(ghTagValidator.Object)
                .AddSingleton<ISqlBarClient, SqlBarClient>()
                .AddSingleton<IDistributedLock, DistributedLock>()
                .AddSingleton<IRedisCacheFactory, MockRedisCacheFactory>();
        }


        public static async Task<Func<IServiceProvider, Task>> DataContext(IServiceCollection collection)
        {
            var connectionString = await SharedData.Database.GetConnectionString();
            collection.AddDbContext<BuildAssetRegistryContext>(options =>
            {
                options.UseSqlServer(connectionString);
                options.EnableServiceProviderCaching(false);
            });
            collection.AddSingleton<IInstallationLookup, BuildAssetRegistryInstallationLookup>();

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

        public static Func<IServiceProvider, ConfigurationIngestionController> ConfigurationIngestionController(IServiceCollection collection)
        {
            collection.AddSingleton<ConfigurationIngestionController>();
            return s => s.GetRequiredService<ConfigurationIngestionController>();
        }
    }

    // Copied from GitHubClaimsResolverTests; could refactor if needed in another place
    private static MockOrg MockOrganization(int id, string login)
    {
        return new MockOrg(id, login);
    }
}

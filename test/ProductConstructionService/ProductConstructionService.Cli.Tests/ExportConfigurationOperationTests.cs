// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using ProductConstructionService.Cli.Operations;
using ProductConstructionService.Cli.Options;
using YamlDotNet.Serialization;

namespace ProductConstructionService.Cli.Tests;

public class ExportConfigurationOperationTests
{
    private const string TestSubscriptionId = "00000000-0000-0000-0000-000000000001";
    private static readonly Guid TestSubscriptionGuid = Guid.Parse(TestSubscriptionId);

    private Mock<IProductConstructionServiceApi> _mockApi = null!;
    private Mock<IFileSystem> _mockFileSystem = null!;
    private Mock<ISubscriptions> _mockSubscriptionsClient = null!;
    private Mock<IChannels> _mockChannelsClient = null!;
    private Mock<IDefaultChannels> _mockDefaultChannelsClient = null!;
    private Mock<IRepository> _mockRepositoryClient = null!;
    private ExportConfigurationOptions _options = null!;
    private ExportConfigurationOperation _operation = null!;

    [SetUp]
    public void Setup()
    {
        _mockApi = new Mock<IProductConstructionServiceApi>();
        _mockFileSystem = new Mock<IFileSystem>();
        _mockSubscriptionsClient = new Mock<ISubscriptions>();
        _mockChannelsClient = new Mock<IChannels>();
        _mockDefaultChannelsClient = new Mock<IDefaultChannels>();
        _mockRepositoryClient = new Mock<IRepository>();

        _mockApi.Setup(api => api.Subscriptions).Returns(_mockSubscriptionsClient.Object);
        _mockApi.Setup(api => api.Channels).Returns(_mockChannelsClient.Object);
        _mockApi.Setup(api => api.DefaultChannels).Returns(_mockDefaultChannelsClient.Object);
        _mockApi.Setup(api => api.Repository).Returns(_mockRepositoryClient.Object);

        _options = new ExportConfigurationOptions 
        { 
            ExportPath = "/test/export",
            IsCi = false,
            PcsUri = "https://test.pcs.com"
        };
        _operation = new ExportConfigurationOperation(_mockApi.Object, _options, _mockFileSystem.Object);
    }

    [Test]
    public async Task ExportSubscriptions_CallsApiAndWritesFiles()
    {
        // Arrange
        var testSubscriptions = new List<Subscription>
        {
            CreateTestSubscription()
        };
        _mockSubscriptionsClient.Setup(s => s.ListSubscriptionsAsync(
            default, default, default, default, default, default, default, default))
            .ReturnsAsync(testSubscriptions);
        SetupEmptyMockData(excludeSubscriptions: true);

        string? writtenContent = null;
        _mockFileSystem.Setup(fs => fs.WriteToFile(
            It.IsAny<string>(),
            It.IsAny<string>()))
            .Callback<string, string>((path, content) => {
                if (path.Contains("subscription"))
                    writtenContent = content;
            });
        
        // Setup CreateDirectory to not throw
        _mockFileSystem.Setup(fs => fs.CreateDirectory(It.IsAny<string>()));

        // Act
        await _operation.RunAsync();

        // Assert
        _mockSubscriptionsClient.Verify(s => s.ListSubscriptionsAsync(
            default, default, default, default, default, default, default, default), Times.Once);
        _mockFileSystem.Verify(fs => fs.WriteToFile(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.AtLeast(1));
        
        // Verify YAML content contains all subscription properties
        var expectedYaml = $"""
            - Id: {TestSubscriptionId}
              Channel: test-channel
              Source Repository URL: https://github.com/test/repo
              Target Repository URL: https://github.com/target/repo
              Target Branch: main
              Update Frequency: everyDay
              Batchable: true
              Excluded Assets:
              - test-asset
              Pull Request Failure Notification Tags: test-tag
              Source Enabled: true
              Source Directory: src
              Target Directory: target

            """;

        writtenContent.Should().Be(expectedYaml);
    }

    [Test]
    public async Task ExportSubscriptions_WithMinimalProperties_CallsApiAndWritesFiles()
    {
        // Arrange
        var testSubscriptions = new List<Subscription>
        {
            CreateMinimalTestSubscription()
        };
        _mockSubscriptionsClient.Setup(s => s.ListSubscriptionsAsync(
            default, default, default, default, default, default, default, default))
            .ReturnsAsync(testSubscriptions);
        SetupEmptyMockData(excludeSubscriptions: true);

        string? writtenContent = null;
        _mockFileSystem.Setup(fs => fs.WriteToFile(
            It.IsAny<string>(),
            It.IsAny<string>()))
            .Callback<string, string>((path, content) => {
                if (path.Contains("subscription"))
                    writtenContent = content;
            });

        // Setup CreateDirectory to not throw
        _mockFileSystem.Setup(fs => fs.CreateDirectory(It.IsAny<string>()));

        // Act
        await _operation.RunAsync();

        // Assert
        _mockSubscriptionsClient.Verify(s => s.ListSubscriptionsAsync(
            default, default, default, default, default, default, default, default), Times.Once);
        _mockFileSystem.Verify(fs => fs.WriteToFile(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.AtLeast(1));

        // Verify YAML content contains all subscription properties with minimal/empty values
        var expectedYaml = $"""
            - Id: {TestSubscriptionId}
              Enabled: false
              Channel: test-channel
              Source Repository URL: https://github.com/test/repo
              Target Repository URL: https://github.com/target/repo
              Target Branch: main
              Update Frequency: everyDay

            """;

        writtenContent.Should().Be(expectedYaml);

        // Deserialize back to SubscriptionYaml to verify round-trip
        var deserializer = new DeserializerBuilder().Build();
        var deserialized = deserializer.Deserialize<List<SubscriptionYaml>>(writtenContent!);

        deserialized.Should().HaveCount(1);
        var subscription = deserialized[0];
        subscription.Id.Should().Be(TestSubscriptionGuid);
        subscription.Enabled.Should().BeFalse();
        subscription.Channel.Should().Be("test-channel");
        subscription.SourceRepository.Should().Be("https://github.com/test/repo");
        subscription.TargetRepository.Should().Be("https://github.com/target/repo");
        subscription.TargetBranch.Should().Be("main");
        subscription.UpdateFrequency.Should().Be(UpdateFrequency.EveryDay);
        subscription.Batchable.Should().BeFalse();
        subscription.SourceEnabled.Should().BeFalse();
        subscription.SourceDirectory.Should().BeNull();
        subscription.TargetDirectory.Should().BeNull();
        subscription.FailureNotificationTags.Should().BeNull();
        subscription.ExcludedAssets.Should().BeEmpty();
        subscription.MergePolicies.Should().BeEmpty();
    }

    [Test]
    public async Task ExportChannels_CallsApiAndWritesFiles()
    {
        // Arrange
        var testChannels = new List<Channel>
        {
            CreateTestChannel()
        };
        _mockChannelsClient.Setup(c => c.ListChannelsAsync(default))
            .ReturnsAsync(testChannels);
        SetupEmptyMockData(excludeChannels: true);

        string? writtenContent = null;
        _mockFileSystem.Setup(fs => fs.WriteToFile(
            It.IsAny<string>(),
            It.IsAny<string>()))
            .Callback<string, string>((path, content) => {
                if (path.Contains("channel"))
                    writtenContent = content;
            });
        
        // Setup CreateDirectory to not throw
        _mockFileSystem.Setup(fs => fs.CreateDirectory(It.IsAny<string>()));

        // Act
        await _operation.RunAsync();

        // Assert
        _mockChannelsClient.Verify(c => c.ListChannelsAsync(default), Times.Once);
        _mockFileSystem.Verify(fs => fs.WriteToFile(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.AtLeast(1));
        
        // Verify YAML content contains all channel properties
        var expectedYaml = """
            - Name: test-channel
              Classification: test

            """;

        writtenContent.Should().Be(expectedYaml);
    }

    private void SetupEmptyMockData(bool excludeSubscriptions = false, bool excludeChannels = false)
    {
        if (!excludeSubscriptions)
        {
            _mockSubscriptionsClient.Setup(s => s.ListSubscriptionsAsync(
                default, default, default, default, default, default, default, default))
                .ReturnsAsync(new List<Subscription>());
        }

        if (!excludeChannels)
        {
            _mockChannelsClient.Setup(c => c.ListChannelsAsync(default))
                .ReturnsAsync(new List<Channel>());
        }

        _mockDefaultChannelsClient.Setup(dc => dc.ListAsync(default))
            .ReturnsAsync(new List<DefaultChannel>());

        _mockRepositoryClient.Setup(r => r.ListRepositoriesAsync(default))
            .ReturnsAsync(new List<RepositoryBranch>());

        // Setup file system operations
        _mockFileSystem.Setup(fs => fs.CreateDirectory(It.IsAny<string>()));
        _mockFileSystem.Setup(fs => fs.WriteToFile(It.IsAny<string>(), It.IsAny<string>()));
    }

    private static Subscription CreateTestSubscription()
    {
        var subscription = new Subscription(
            id: TestSubscriptionGuid,
            enabled: true,
            sourceEnabled: true,
            sourceRepository: "https://github.com/test/repo",
            targetRepository: "https://github.com/target/repo",
            targetBranch: "main",
            sourceDirectory: "src",
            targetDirectory: "target",
            pullRequestFailureNotificationTags: "test-tag",
            excludedAssets: new List<string> { "test-asset" }
        );
        
        // Set the mutable properties
        subscription.Channel = CreateTestChannel();
        subscription.Policy = new SubscriptionPolicy(batchable: true, updateFrequency: UpdateFrequency.EveryDay)
        {
            MergePolicies = new List<MergePolicy>()
        };
        
        return subscription;
    }

    private static Subscription CreateMinimalTestSubscription()
    {
        var subscription = new Subscription(
            id: TestSubscriptionGuid,
            enabled: false,
            sourceEnabled: false,
            sourceRepository: "https://github.com/test/repo",
            targetRepository: "https://github.com/target/repo",
            targetBranch: "main",
            sourceDirectory: null,
            targetDirectory: null,
            pullRequestFailureNotificationTags: null,
            excludedAssets: new List<string>()
        );

        // Set the mutable properties
        subscription.Channel = CreateTestChannel();
        subscription.Policy = new SubscriptionPolicy(batchable: false, updateFrequency: UpdateFrequency.EveryDay)
        {
            MergePolicies = new List<MergePolicy>()
        };

        return subscription;
    }

    private static Channel CreateTestChannel()
    {
        return new Channel(
            id: 1,
            name: "test-channel",
            classification: "test"
        );
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class AddChannelOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<AddChannelOperation>> _loggerMock = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<AddChannelOperation>>();
    }

    [Test]
    public async Task AddChannelOperation_WithConfigRepo_CreatesChannelFile()
    {
        // Arrange
        var expectedChannel = new ChannelYaml
        {
            Name = ".NET 9",
            Classification = "product"
        };
        var testBranch = GetTestBranch();
        var expectedFilePath = ConfigFilePathResolver.GetDefaultChannelFilePath(expectedChannel);

        var options = CreateAddChannelOptions(expectedChannel, configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify file was created at the expected path
        await CheckoutBranch(testBranch);
        var fullExpectedPath = Path.Combine(ConfigurationRepoPath, expectedFilePath);
        File.Exists(fullExpectedPath).Should().BeTrue($"Expected file at {fullExpectedPath}");

        // Deserialize and verify channel properties
        var channels = await DeserializeChannelsAsync(fullExpectedPath);
        channels.Should().HaveCount(1);

        var actualChannel = channels[0];
        actualChannel.Name.Should().Be(expectedChannel.Name);
        actualChannel.Classification.Should().Be(expectedChannel.Classification);
    }

    [Test]
    public async Task AddChannelOperation_WithConfigRepo_AppendsToExistingFile()
    {
        // Arrange
        var expectedChannel = new ChannelYaml
        {
            Name = ".NET 9",
            Classification = "product"
        };
        var testBranch = GetTestBranch();

        const string configFileName = ".net-9.yml";

        // Create existing channel file at the expected location
        var existingContent = """
            - Name: .NET 8
              Classification: product
            """;
        var configFilePath = new UnixPath(ConfigFilePathResolver.ChannelFolderPath) / configFileName;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        var options = CreateAddChannelOptions(
            expectedChannel,
            configurationBranch: testBranch,
            configurationFilePath: configFilePath.ToString());

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Deserialize and verify both channels are present
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var channels = await DeserializeChannelsAsync(fullPath);
        channels.Should().HaveCount(2);

        channels.Should().Contain(c => c.Name == ".NET 8");
        channels.Should().Contain(c => c.Name == expectedChannel.Name);
    }

    [Test]
    public async Task AddChannelOperation_WithConfigRepo_FailsWhenEquivalentChannelExistsInBar()
    {
        // Arrange
        var channelToAdd = new ChannelYaml
        {
            Name = ".NET 9",
            Classification = "product"
        };

        // Setup an existing channel returned by BAR
        var existingChannel = new Channel(
            id: 42,
            name: channelToAdd.Name,
            classification: channelToAdd.Classification);

        BarClientMock
            .Setup(x => x.GetChannelsAsync())
            .ReturnsAsync([existingChannel]);

        var options = CreateAddChannelOptions(channelToAdd, configurationBranch: GetTestBranch());
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);

        // No new yml files should have been created (only the README.md from setup)
        var ymlFiles = Directory.GetFiles(ConfigurationRepoPath, "*.yml", SearchOption.AllDirectories);
        ymlFiles.Should().BeEmpty();
    }

    [Test]
    public async Task AddChannelOperation_WithConfigRepo_FailsWhenEquivalentChannelExistsInYamlFile()
    {
        // Arrange
        var channelToAdd = new ChannelYaml
        {
            Name = ".NET 9",
            Classification = "product"
        };

        const string configFileName = ".net-9.yml";

        // Create existing channel file with an equivalent channel (same name)
        var existingContent = $"""
            - Name: {channelToAdd.Name}
              Classification: {channelToAdd.Classification}
            """;
        var configFilePath = new UnixPath(ConfigFilePathResolver.ChannelFolderPath) / configFileName;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        var options = CreateAddChannelOptions(
            channelToAdd,
            configurationBranch: GetTestBranch(),
            configurationFilePath: configFilePath.ToString());

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);

        // Verify the file still only contains the original channel
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var channels = await DeserializeChannelsAsync(fullPath);
        channels.Should().HaveCount(1);
        channels[0].Name.Should().Be(channelToAdd.Name);
    }

    private AddChannelCommandLineOptions CreateAddChannelOptions(
        ChannelYaml channel,
        string? configurationBranch = null,
        string configurationBaseBranch = DefaultBranch,
        string? configurationFilePath = null,
        bool noPr = true)
    {
        return new AddChannelCommandLineOptions
        {
            Name = channel.Name,
            Classification = channel.Classification,
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = configurationBranch,
            ConfigurationBaseBranch = configurationBaseBranch,
            ConfigurationFilePath = configurationFilePath,
            NoPr = noPr
        };
    }

    private AddChannelOperation CreateOperation(AddChannelCommandLineOptions options)
    {
        return new AddChannelOperation(
            options,
            BarClientMock.Object,
            ConfigurationRepositoryManager,
            _loggerMock.Object);
    }

    private static async Task<List<ChannelYaml>> DeserializeChannelsAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        return YamlDeserializer.Deserialize<List<ChannelYaml>>(content) ?? [];
    }
}

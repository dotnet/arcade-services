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
public class UpdateChannelOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<UpdateChannelOperation>> _loggerMock = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<UpdateChannelOperation>>();
    }

    [Test]
    public async Task UpdateChannelOperation_WithConfigRepo_UpdatesChannelClassification()
    {
        // Arrange
        const string channelName = ".NET 9";
        const string originalClassification = "dev";
        const string updatedClassification = "product";
        var testBranch = GetTestBranch();

        var channelYaml = new ChannelYaml
        {
            Name = channelName,
            Classification = originalClassification
        };

        var configFilePath = ConfigFilePathResolver.GetDefaultChannelFilePath(channelYaml);
        await CreateFileInConfigRepoAsync(configFilePath, CreateChannelYamlContent(channelYaml));

        var channel = CreateTestChannel(1, channelName, originalClassification);
        SetupGetChannelAsync(channel);

        var options = CreateUpdateChannelOptions(
            id: 1,
            classification: updatedClassification,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the channel was updated
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath);
        File.Exists(fullPath).Should().BeTrue("File should still exist");

        var channels = await DeserializeChannelsAsync(fullPath);
        channels.Should().HaveCount(1);

        var updatedChannel = channels[0];
        updatedChannel.Name.Should().Be(channelName);
        updatedChannel.Classification.Should().Be(updatedClassification);
    }

    [Test]
    public async Task UpdateChannelOperation_WithConfigRepo_UpdatesChannelInFileWithMultipleChannels()
    {
        // Arrange
        const string channel1Name = ".NET 8";
        const string channel2Name = ".NET 9";
        var testBranch = GetTestBranch();

        var channel1Yaml = new ChannelYaml { Name = channel1Name, Classification = "dev" };
        var channel2Yaml = new ChannelYaml { Name = channel2Name, Classification = "preview" };

        // Create file with both channels
        var configFilePath = new UnixPath(ConfigFilePathResolver.ChannelFolderPath) / ".net.yml";
        var existingContent = $"""
            {CreateChannelYamlContent(channel1Yaml)}

            {CreateChannelYamlContent(channel2Yaml)}
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        var channel = CreateTestChannel(1, channel2Name, "preview");
        SetupGetChannelAsync(channel);

        var options = CreateUpdateChannelOptions(
            id: 1,
            classification: "product",
            configurationBranch: testBranch,
            configurationFilePath: configFilePath.ToString());
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the channel was updated
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var channels = await DeserializeChannelsAsync(fullPath);
        channels.Should().HaveCount(2);

        // Verify channel 2 was updated
        var updatedChannel = channels.Find(c => c.Name == channel2Name);
        updatedChannel.Should().NotBeNull();
        updatedChannel!.Classification.Should().Be("product");

        // Verify channel 1 was not modified
        var unchangedChannel = channels.Find(c => c.Name == channel1Name);
        unchangedChannel.Should().NotBeNull();
        unchangedChannel!.Classification.Should().Be("dev");
    }

    [Test]
    public async Task UpdateChannelOperation_WithConfigRepo_FindsChannelInNonDefaultFile()
    {
        // Arrange - channel is in a file that doesn't match the default naming convention
        const string channelName = ".NET 9";
        var testBranch = GetTestBranch();

        var channelYaml = new ChannelYaml { Name = channelName, Classification = "dev" };

        // Create file at a custom path
        var customFilePath = new UnixPath(ConfigFilePathResolver.ChannelFolderPath) / "custom-channels.yml";
        await CreateFileInConfigRepoAsync(customFilePath.ToString(), CreateChannelYamlContent(channelYaml));

        var channel = CreateTestChannel(1, channelName, "dev");
        SetupGetChannelAsync(channel);

        // Note: we do NOT specify configurationFilePath - the operation should find it by searching
        var options = CreateUpdateChannelOptions(
            id: 1,
            classification: "product",
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the channel was updated in the correct file
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, customFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist");

        var channels = await DeserializeChannelsAsync(fullPath);
        channels.Should().HaveCount(1);
        channels[0].Name.Should().Be(channelName);
        channels[0].Classification.Should().Be("product");
    }

    [Test]
    public async Task UpdateChannelOperation_WithConfigRepo_UsesSpecifiedConfigFilePath()
    {
        // Arrange
        const string channelName = ".NET 9";
        var testBranch = GetTestBranch();

        var channelYaml = new ChannelYaml { Name = channelName, Classification = "dev" };

        // Create channel file at a custom path
        var specifiedFilePath = new UnixPath(ConfigFilePathResolver.ChannelFolderPath) / "my-custom-channels.yml";
        await CreateFileInConfigRepoAsync(specifiedFilePath.ToString(), CreateChannelYamlContent(channelYaml));

        var channel = CreateTestChannel(1, channelName, "dev");
        SetupGetChannelAsync(channel);

        var options = CreateUpdateChannelOptions(
            id: 1,
            classification: "product",
            configurationBranch: testBranch,
            configurationFilePath: specifiedFilePath.ToString());
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the channel was updated
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, specifiedFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist");

        var channels = await DeserializeChannelsAsync(fullPath);
        channels.Should().HaveCount(1);
        channels[0].Name.Should().Be(channelName);
        channels[0].Classification.Should().Be("product");
    }

    [Test]
    public async Task UpdateChannelOperation_WithConfigRepo_FailsWhenChannelNotFound()
    {
        // Arrange
        const string channelName = ".NET 9";
        var testBranch = GetTestBranch();

        // Create a different channel in the config repo
        var differentChannelYaml = new ChannelYaml { Name = ".NET 8", Classification = "dev" };
        var configFilePath = ConfigFilePathResolver.GetDefaultChannelFilePath(differentChannelYaml);
        await CreateFileInConfigRepoAsync(configFilePath, CreateChannelYamlContent(differentChannelYaml));

        var channel = CreateTestChannel(1, channelName, "dev");
        SetupGetChannelAsync(channel);

        var options = CreateUpdateChannelOptions(
            id: 1,
            classification: "product",
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);
    }

    [Test]
    public async Task UpdateChannelOperation_WithConfigRepo_FailsWhenChannelNameIsChanged()
    {
        // Arrange
        const string originalChannelName = ".NET 9";
        const string newChannelName = ".NET 10";
        var testBranch = GetTestBranch();

        var channelYaml = new ChannelYaml { Name = originalChannelName, Classification = "dev" };
        var configFilePath = ConfigFilePathResolver.GetDefaultChannelFilePath(channelYaml);
        await CreateFileInConfigRepoAsync(configFilePath, CreateChannelYamlContent(channelYaml));

        var channel = CreateTestChannel(1, originalChannelName, "dev");
        SetupGetChannelAsync(channel);

        // Attempt to change the name
        var options = CreateUpdateChannelOptions(
            id: 1,
            name: newChannelName,
            classification: "product",
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);

        // Verify the channel was not modified
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath);
        var channels = await DeserializeChannelsAsync(fullPath);
        channels.Should().HaveCount(1);
        channels[0].Name.Should().Be(originalChannelName);
        channels[0].Classification.Should().Be("dev");
    }

    [Test]
    public async Task UpdateChannelOperation_WithConfigRepo_KeepsExistingClassificationWhenNotProvided()
    {
        // Arrange
        const string channelName = ".NET 9";
        const string existingClassification = "dev";
        var testBranch = GetTestBranch();

        var channelYaml = new ChannelYaml { Name = channelName, Classification = existingClassification };
        var configFilePath = ConfigFilePathResolver.GetDefaultChannelFilePath(channelYaml);
        await CreateFileInConfigRepoAsync(configFilePath, CreateChannelYamlContent(channelYaml));

        var channel = CreateTestChannel(1, channelName, existingClassification);
        SetupGetChannelAsync(channel);

        // Don't provide classification - it should keep the existing one
        var options = CreateUpdateChannelOptions(
            id: 1,
            name: channelName, // Same name is allowed
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the channel classification was not changed
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath);
        var channels = await DeserializeChannelsAsync(fullPath);
        channels.Should().HaveCount(1);
        channels[0].Name.Should().Be(channelName);
        channels[0].Classification.Should().Be(existingClassification);
    }

    #region Helper methods

    private Channel CreateTestChannel(int id, string name, string classification)
    {
        return new Channel(id, name, classification);
    }

    private static string CreateChannelYamlContent(ChannelYaml channel)
    {
        return $"""
            - Name: {channel.Name}
              Classification: {channel.Classification}
            """;
    }

    private void SetupGetChannelAsync(Channel channel)
    {
        BarClientMock
            .Setup(x => x.GetChannelAsync(channel.Id))
            .ReturnsAsync(channel);
    }

    private UpdateChannelCommandLineOptions CreateUpdateChannelOptions(
        int id,
        string? name = null,
        string? classification = null,
        string? configurationBranch = null,
        string configurationBaseBranch = DefaultBranch,
        string? configurationFilePath = null,
        bool noPr = true)
    {
        return new UpdateChannelCommandLineOptions
        {
            Id = id,
            Name = name,
            Classification = classification,
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = configurationBranch,
            ConfigurationBaseBranch = configurationBaseBranch,
            ConfigurationFilePath = configurationFilePath,
            NoPr = noPr
        };
    }

    private UpdateChannelOperation CreateOperation(UpdateChannelCommandLineOptions options)
    {
        return new UpdateChannelOperation(
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

    #endregion
}

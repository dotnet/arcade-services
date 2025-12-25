// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
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
public class DeleteChannelOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<DeleteChannelOperation>> _loggerMock = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<DeleteChannelOperation>>();
    }

    [Test]
    public async Task DeleteChannelOperation_WithConfigRepo_RemovesChannelFromFile()
    {
        // Arrange
        var channelToDelete = CreateTestChannel("test-channel-1", "test");
        var channelToKeep = CreateTestChannel("test-channel-2", "dev");
        var testBranch = GetTestBranch();

        // Create channel file with both channels - one to delete and one to keep
        var configFilePath = new UnixPath(ConfigFilePathResolver.ChannelFolderPath) / "test-channels.yml";
        var existingContent = $"""
            {CreateChannelYamlContent(channelToDelete)}

            {CreateChannelYamlContent(channelToKeep)}
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupGetChannelsAsync([channelToDelete, channelToKeep]);

        var options = CreateDeleteChannelOptions(
            name: channelToDelete.Name,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify only the remaining channel is in the file
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist with remaining channel");

        var channels = await DeserializeChannelsAsync(fullPath);
        channels.Should().HaveCount(1);
        channels[0].Name.Should().Be(channelToKeep.Name);
        channels[0].Classification.Should().Be(channelToKeep.Classification);
    }

    [Test]
    public async Task DeleteChannelOperation_WithConfigRepo_UsesSpecifiedConfigFilePath()
    {
        // Arrange - when a specific file path is provided, it should be used directly
        var channelToDelete = CreateTestChannel("test-channel-1", "test");
        var channelToKeep = CreateTestChannel("test-channel-2", "dev");
        var testBranch = GetTestBranch();

        // Create channel file at a custom path
        var specifiedFilePath = new UnixPath(ConfigFilePathResolver.ChannelFolderPath) / "my-custom-file.yml";
        var existingContent = $"""
            {CreateChannelYamlContent(channelToDelete)}

            {CreateChannelYamlContent(channelToKeep)}
            """;
        await CreateFileInConfigRepoAsync(specifiedFilePath.ToString(), existingContent);

        SetupGetChannelsAsync([channelToDelete, channelToKeep]);

        var options = CreateDeleteChannelOptions(
            name: channelToDelete.Name,
            configurationBranch: testBranch,
            configurationFilePath: specifiedFilePath.ToString());
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify only the remaining channel is in the file
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, specifiedFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist with remaining channel");

        var channels = await DeserializeChannelsAsync(fullPath);
        channels.Should().HaveCount(1);
        channels[0].Name.Should().Be(channelToKeep.Name);
    }

    [Test]
    public async Task DeleteChannelOperation_WithConfigRepo_FindsChannelInNonDefaultFile()
    {
        // Arrange - channel is in a file that doesn't match the default naming convention
        // so the operation must search through all files in the channels folder
        var channelToDelete = CreateTestChannel("test-channel-1", "test");
        var testBranch = GetTestBranch();

        // Create two files that DON'T contain the channel we're looking for
        // This ensures the search has to go through multiple files
        var unrelatedFile1Path = new UnixPath(ConfigFilePathResolver.ChannelFolderPath) / "aaa-first-file.yml";
        var unrelatedChannel1 = CreateTestChannel("unrelated-channel-1", "dev");
        await CreateFileInConfigRepoAsync(unrelatedFile1Path.ToString(), CreateChannelYamlContent(unrelatedChannel1));

        var unrelatedFile2Path = new UnixPath(ConfigFilePathResolver.ChannelFolderPath) / "bbb-second-file.yml";
        var unrelatedChannel2 = CreateTestChannel("unrelated-channel-2", "dev");
        await CreateFileInConfigRepoAsync(unrelatedFile2Path.ToString(), CreateChannelYamlContent(unrelatedChannel2));

        // Create the file with ONLY the channel we want to delete (file should be deleted after)
        var customFilePath = new UnixPath(ConfigFilePathResolver.ChannelFolderPath) / "zzz-custom-channels.yml";
        await CreateFileInConfigRepoAsync(customFilePath.ToString(), CreateChannelYamlContent(channelToDelete));

        SetupGetChannelsAsync([channelToDelete, unrelatedChannel1, unrelatedChannel2]);

        // Note: we do NOT specify configurationFilePath - the operation should find it by searching
        var options = CreateDeleteChannelOptions(
            name: channelToDelete.Name,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the file was deleted since it had only one channel
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, customFilePath.ToString());
        File.Exists(fullPath).Should().BeFalse("File should be deleted when last channel is removed");

        // Verify the unrelated files were not modified
        var unrelatedFile1FullPath = Path.Combine(ConfigurationRepoPath, unrelatedFile1Path.ToString());
        var unrelatedFile1Channels = await DeserializeChannelsAsync(unrelatedFile1FullPath);
        unrelatedFile1Channels.Should().HaveCount(1);
        unrelatedFile1Channels[0].Name.Should().Be(unrelatedChannel1.Name);

        var unrelatedFile2FullPath = Path.Combine(ConfigurationRepoPath, unrelatedFile2Path.ToString());
        var unrelatedFile2Channels = await DeserializeChannelsAsync(unrelatedFile2FullPath);
        unrelatedFile2Channels.Should().HaveCount(1);
        unrelatedFile2Channels[0].Name.Should().Be(unrelatedChannel2.Name);
    }

    #region Helper methods

    private Channel CreateTestChannel(string name, string classification)
    {
        return new Channel(1, name, classification);
    }

    private static string CreateChannelYamlContent(Channel channel)
    {
        return $"""
            - Name: {channel.Name}
              Classification: {channel.Classification}
            """;
    }

    private void SetupGetChannelsAsync(List<Channel> channels)
    {
        BarClientMock
            .Setup(x => x.GetChannelsAsync())
            .ReturnsAsync(channels);
    }

    private DeleteChannelCommandLineOptions CreateDeleteChannelOptions(
        string name,
        string? configurationBranch = null,
        string configurationBaseBranch = DefaultBranch,
        string? configurationFilePath = null,
        bool noPr = true)
    {
        return new DeleteChannelCommandLineOptions
        {
            Name = name,
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = configurationBranch,
            ConfigurationBaseBranch = configurationBaseBranch,
            ConfigurationFilePath = configurationFilePath,
            NoPr = noPr
        };
    }

    private DeleteChannelOperation CreateOperation(DeleteChannelCommandLineOptions options)
    {
        return new DeleteChannelOperation(
            options,
            _loggerMock.Object,
            BarClientMock.Object,
            ConfigurationRepositoryManager);
    }

    /// <summary>
    /// Deserializes a YAML file containing a list of channels.
    /// </summary>
    private static async Task<List<ChannelYaml>> DeserializeChannelsAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        return YamlDeserializer.Deserialize<List<ChannelYaml>>(content) ?? [];
    }

    #endregion
}

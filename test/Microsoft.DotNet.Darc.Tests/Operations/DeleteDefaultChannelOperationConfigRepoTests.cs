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
public class DeleteDefaultChannelOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<DeleteDefaultChannelOperation>> _loggerMock = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<DeleteDefaultChannelOperation>>();
    }

    [Test]
    public async Task DeleteDefaultChannelOperation_WithConfigRepo_RemovesDefaultChannelFromFile()
    {
        // Arrange
        var channel = "test-channel";
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "refs/heads/main";
        var testBranch = GetTestBranch();

        // Create default channel file with two default channels - one to delete and one to keep
        var defaultChannelToDelete = CreateTestDefaultChannel(repository, branch, channel);
        var defaultChannelToKeep = CreateTestDefaultChannel("https://github.com/dotnet/other-repo", "refs/heads/main", channel);

        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "dotnet-test-repo.yml";
        var existingContent = $"""
            {CreateDefaultChannelYamlContent(defaultChannelToDelete)}

            {CreateDefaultChannelYamlContent(defaultChannelToKeep)}
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupChannel(channel);
        SetupGetDefaultChannelsAsync(defaultChannelToDelete, defaultChannelToKeep);

        var options = CreateDeleteDefaultChannelOptions(
            channel: channel,
            repository: repository,
            branch: branch,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify only the remaining default channel is in the file
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist with remaining default channel");

        var defaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        defaultChannels.Should().HaveCount(1);
        defaultChannels[0].Repository.Should().Be(defaultChannelToKeep.Repository);
        defaultChannels[0].Branch.Should().Be(defaultChannelToKeep.Branch);
        defaultChannels[0].Channel.Should().Be(defaultChannelToKeep.Channel.Name);
    }

    [Test]
    public async Task DeleteDefaultChannelOperation_WithConfigRepo_DeletesFileWhenLastDefaultChannelRemoved()
    {
        // Arrange
        var channel = "test-channel";
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "refs/heads/main";
        var testBranch = GetTestBranch();

        // Create default channel file with only one default channel
        var defaultChannelToDelete = CreateTestDefaultChannel(repository, branch, channel);

        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "dotnet-test-repo.yml";
        var existingContent = CreateDefaultChannelYamlContent(defaultChannelToDelete);
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupChannel(channel);
        SetupGetDefaultChannelsAsync(defaultChannelToDelete);

        var options = CreateDeleteDefaultChannelOptions(
            channel: channel,
            repository: repository,
            branch: branch,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the file was deleted since it had only one default channel
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        File.Exists(fullPath).Should().BeFalse("File should be deleted when last default channel is removed");
    }

    [Test]
    public async Task DeleteDefaultChannelOperation_WithConfigRepo_UsesSpecifiedConfigFilePath()
    {
        // Arrange - when a specific file path is provided, it should be used directly
        var channel = "test-channel";
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "refs/heads/main";
        var testBranch = GetTestBranch();

        var defaultChannelToDelete = CreateTestDefaultChannel(repository, branch, channel);
        var defaultChannelToKeep = CreateTestDefaultChannel("https://github.com/dotnet/other-repo", "refs/heads/main", channel);

        // Create default channel file at a custom path
        var specifiedFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "my-custom-file.yml";
        var existingContent = $"""
            {CreateDefaultChannelYamlContent(defaultChannelToDelete)}

            {CreateDefaultChannelYamlContent(defaultChannelToKeep)}
            """;
        await CreateFileInConfigRepoAsync(specifiedFilePath.ToString(), existingContent);

        SetupChannel(channel);
        SetupGetDefaultChannelsAsync(defaultChannelToDelete, defaultChannelToKeep);

        var options = CreateDeleteDefaultChannelOptions(
            channel: channel,
            repository: repository,
            branch: branch,
            configurationBranch: testBranch,
            configurationFilePath: specifiedFilePath.ToString());
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify only the remaining default channel is in the file
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, specifiedFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist with remaining default channel");

        var defaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        defaultChannels.Should().HaveCount(1);
        defaultChannels[0].Repository.Should().Be(defaultChannelToKeep.Repository);
    }

    [Test]
    public async Task DeleteDefaultChannelOperation_WithConfigRepo_FindsDefaultChannelInNonDefaultFile()
    {
        // Arrange - default channel is in a file that doesn't match the default naming convention
        // so the operation must search through all files in the default-channels folder
        var channel = "test-channel";
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "refs/heads/main";
        var testBranch = GetTestBranch();

        var defaultChannelToDelete = CreateTestDefaultChannel(repository, branch, channel);

        // Create two files that DON'T contain the default channel we're looking for
        // This ensures the search has to go through multiple files
        var unrelatedFile1Path = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "aaa-first-file.yml";
        var unrelatedDefaultChannel1 = CreateTestDefaultChannel(
            "https://github.com/dotnet/unrelated-repo-1",
            "refs/heads/main",
            "other-channel-1");
        await CreateFileInConfigRepoAsync(unrelatedFile1Path.ToString(), CreateDefaultChannelYamlContent(unrelatedDefaultChannel1));

        var unrelatedFile2Path = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "bbb-second-file.yml";
        var unrelatedDefaultChannel2 = CreateTestDefaultChannel(
            "https://github.com/dotnet/unrelated-repo-2",
            "refs/heads/release/1.0",
            "other-channel-2");
        await CreateFileInConfigRepoAsync(unrelatedFile2Path.ToString(), CreateDefaultChannelYamlContent(unrelatedDefaultChannel2));

        // Create the file with ONLY the default channel we want to delete (file should be deleted after)
        var customFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "zzz-custom-defaults.yml";
        await CreateFileInConfigRepoAsync(customFilePath.ToString(), CreateDefaultChannelYamlContent(defaultChannelToDelete));

        SetupChannel(channel);
        SetupGetDefaultChannelsAsync(defaultChannelToDelete, unrelatedDefaultChannel1, unrelatedDefaultChannel2);

        // Note: we do NOT specify configurationFilePath - the operation should find it by searching
        var options = CreateDeleteDefaultChannelOptions(
            channel: channel,
            repository: repository,
            branch: branch,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the file was deleted since it had only one default channel
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, customFilePath.ToString());
        File.Exists(fullPath).Should().BeFalse("File should be deleted when last default channel is removed");

        // Verify the unrelated files were not modified
        var unrelatedFile1FullPath = Path.Combine(ConfigurationRepoPath, unrelatedFile1Path.ToString());
        var unrelatedFile1DefaultChannels = await DeserializeDefaultChannelsAsync(unrelatedFile1FullPath);
        unrelatedFile1DefaultChannels.Should().HaveCount(1);
        unrelatedFile1DefaultChannels[0].Repository.Should().Be(unrelatedDefaultChannel1.Repository);

        var unrelatedFile2FullPath = Path.Combine(ConfigurationRepoPath, unrelatedFile2Path.ToString());
        var unrelatedFile2DefaultChannels = await DeserializeDefaultChannelsAsync(unrelatedFile2FullPath);
        unrelatedFile2DefaultChannels.Should().HaveCount(1);
        unrelatedFile2DefaultChannels[0].Repository.Should().Be(unrelatedDefaultChannel2.Repository);
    }

    #region Helper methods

    private DefaultChannel CreateTestDefaultChannel(
        string repository,
        string branch,
        string channelName,
        int channelId = 1,
        bool enabled = true)
    {
        return new DefaultChannel(
            id: 1,
            repository: repository,
            enabled: enabled)
        {
            Branch = branch,
            Channel = new Channel(channelId, channelName, "test")
        };
    }

    private static string CreateDefaultChannelYamlContent(DefaultChannel defaultChannel)
    {
        return $"""
            - Repository: {defaultChannel.Repository}
              Branch: {defaultChannel.Branch}
              Channel: {defaultChannel.Channel.Name}
              Enabled: {defaultChannel.Enabled.ToString().ToLower()}
            """;
    }

    private void SetupGetDefaultChannelsAsync(params DefaultChannel[] defaultChannels)
    {
        BarClientMock
            .Setup(x => x.GetDefaultChannelsAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new List<DefaultChannel>(defaultChannels));
    }

    private DeleteDefaultChannelCommandLineOptions CreateDeleteDefaultChannelOptions(
        string? channel = null,
        string? repository = null,
        string? branch = null,
        int id = -1,
        string? configurationBranch = null,
        string configurationBaseBranch = DefaultBranch,
        string? configurationFilePath = null,
        bool noPr = true)
    {
        return new DeleteDefaultChannelCommandLineOptions
        {
            Channel = channel ?? string.Empty,
            Repository = repository ?? string.Empty,
            Branch = branch ?? string.Empty,
            Id = id,
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = configurationBranch,
            ConfigurationBaseBranch = configurationBaseBranch,
            ConfigurationFilePath = configurationFilePath,
            NoPr = noPr
        };
    }

    private DeleteDefaultChannelOperation CreateOperation(DeleteDefaultChannelCommandLineOptions options)
    {
        return new DeleteDefaultChannelOperation(
            options,
            BarClientMock.Object,
            ConfigurationRepositoryManager,
            _loggerMock.Object);
    }

    /// <summary>
    /// Deserializes a YAML file containing a list of default channels.
    /// </summary>
    private static async Task<List<DefaultChannelYaml>> DeserializeDefaultChannelsAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        return YamlDeserializer.Deserialize<List<DefaultChannelYaml>>(content) ?? [];
    }

    #endregion
}

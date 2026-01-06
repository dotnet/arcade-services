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
public class DefaultChannelStatusOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<DefaultChannelStatusOperation>> _loggerMock = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<DefaultChannelStatusOperation>>();
    }

    [Test]
    public async Task DefaultChannelStatusOperation_WithConfigRepo_DisablesDefaultChannel()
    {
        // Arrange
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "main";
        var channel = "test-channel";
        var testBranch = GetTestBranch();

        // Create default channel file with enabled default channel
        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "dotnet-test-repo.yml";
        var existingContent = $"""
            - Repository: {repository}
              Branch: {branch}
              Channel: {channel}
              Enabled: true
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupGetDefaultChannelsAsync(repository, branch, channel, enabled: true);

        var options = CreateDefaultChannelStatusOptions(
            repository: repository,
            branch: branch,
            channel: channel,
            disable: true,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the default channel was disabled
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist");

        var defaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        defaultChannels.Should().HaveCount(1);
        defaultChannels[0].Repository.Should().Be(repository);
        defaultChannels[0].Branch.Should().Be(branch);
        defaultChannels[0].Channel.Should().Be(channel);
        defaultChannels[0].Enabled.Should().BeFalse();
    }

    [Test]
    public async Task DefaultChannelStatusOperation_WithConfigRepo_EnablesDefaultChannel()
    {
        // Arrange
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "main";
        var channel = "test-channel";
        var testBranch = GetTestBranch();

        // Create default channel file with disabled default channel
        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "dotnet-test-repo.yml";
        var existingContent = $"""
            - Repository: {repository}
              Branch: {branch}
              Channel: {channel}
              Enabled: false
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupGetDefaultChannelsAsync(repository, branch, channel, enabled: false);

        var options = CreateDefaultChannelStatusOptions(
            repository: repository,
            branch: branch,
            channel: channel,
            enable: true,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the default channel was enabled
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist");

        var defaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        defaultChannels.Should().HaveCount(1);
        defaultChannels[0].Repository.Should().Be(repository);
        defaultChannels[0].Branch.Should().Be(branch);
        defaultChannels[0].Channel.Should().Be(channel);
        defaultChannels[0].Enabled.Should().BeTrue();
    }

    [Test]
    public async Task DefaultChannelStatusOperation_WithConfigRepo_UpdatesOnlyTargetDefaultChannel()
    {
        // Arrange
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "main";
        var channel = "test-channel";
        var testBranch = GetTestBranch();

        // Create default channel file with multiple default channels
        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "dotnet-test-repo.yml";
        var existingContent = $"""
            - Repository: {repository}
              Branch: {branch}
              Channel: {channel}
              Enabled: true

            - Repository: {repository}
              Branch: release/8.0
              Channel: other-channel
              Enabled: true
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupGetDefaultChannelsAsync(repository, branch, channel, enabled: true);

        var options = CreateDefaultChannelStatusOptions(
            repository: repository,
            branch: branch,
            channel: channel,
            disable: true,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify only the target default channel was disabled
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var defaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        defaultChannels.Should().HaveCount(2);

        var updatedChannel = defaultChannels.Find(dc => dc.Branch == branch && dc.Channel == channel);
        updatedChannel.Should().NotBeNull();
        updatedChannel!.Enabled.Should().BeFalse();

        var otherChannel = defaultChannels.Find(dc => dc.Branch == "release/8.0" && dc.Channel == "other-channel");
        otherChannel.Should().NotBeNull();
        otherChannel!.Enabled.Should().BeTrue();
    }

    [Test]
    public async Task DefaultChannelStatusOperation_WithConfigRepo_FindsDefaultChannelInNonDefaultFile()
    {
        // Arrange - default channel is in a file that doesn't match the default naming convention
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "main";
        var channel = "test-channel";
        var testBranch = GetTestBranch();

        // Create two files that DON'T contain the default channel we're looking for
        var unrelatedFile1Path = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "aaa-first-file.yml";
        var unrelatedContent1 = """
            - Repository: https://github.com/dotnet/unrelated-repo-1
              Branch: main
              Channel: unrelated-channel-1
              Enabled: true
            """;
        await CreateFileInConfigRepoAsync(unrelatedFile1Path.ToString(), unrelatedContent1);

        var unrelatedFile2Path = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "bbb-second-file.yml";
        var unrelatedContent2 = """
            - Repository: https://github.com/dotnet/unrelated-repo-2
              Branch: main
              Channel: unrelated-channel-2
              Enabled: true
            """;
        await CreateFileInConfigRepoAsync(unrelatedFile2Path.ToString(), unrelatedContent2);

        // Create the file with the default channel we want to update
        var customFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "zzz-custom-default-channels.yml";
        var targetContent = $"""
            - Repository: {repository}
              Branch: {branch}
              Channel: {channel}
              Enabled: true
            """;
        await CreateFileInConfigRepoAsync(customFilePath.ToString(), targetContent);

        SetupGetDefaultChannelsAsync(repository, branch, channel, enabled: true);

        // Note: we do NOT specify configurationFilePath - the operation should find it by searching
        var options = CreateDefaultChannelStatusOptions(
            repository: repository,
            branch: branch,
            channel: channel,
            disable: true,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the default channel was updated in the correct file
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, customFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist");

        var defaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        defaultChannels.Should().HaveCount(1);
        defaultChannels[0].Repository.Should().Be(repository);
        defaultChannels[0].Branch.Should().Be(branch);
        defaultChannels[0].Channel.Should().Be(channel);
        defaultChannels[0].Enabled.Should().BeFalse();

        // Verify the unrelated files were not modified
        var unrelatedFile1FullPath = Path.Combine(ConfigurationRepoPath, unrelatedFile1Path.ToString());
        var unrelatedFile1Channels = await DeserializeDefaultChannelsAsync(unrelatedFile1FullPath);
        unrelatedFile1Channels.Should().HaveCount(1);
        unrelatedFile1Channels[0].Enabled.Should().BeTrue();

        var unrelatedFile2FullPath = Path.Combine(ConfigurationRepoPath, unrelatedFile2Path.ToString());
        var unrelatedFile2Channels = await DeserializeDefaultChannelsAsync(unrelatedFile2FullPath);
        unrelatedFile2Channels.Should().HaveCount(1);
        unrelatedFile2Channels[0].Enabled.Should().BeTrue();
    }

    [Test]
    public async Task DefaultChannelStatusOperation_WithConfigRepo_UsesSpecifiedConfigFilePath()
    {
        // Arrange
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "main";
        var channel = "test-channel";
        var testBranch = GetTestBranch();

        // Create default channel file at a custom path
        var specifiedFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "my-custom-file.yml";
        var content = $"""
            - Repository: {repository}
              Branch: {branch}
              Channel: {channel}
              Enabled: true
            """;
        await CreateFileInConfigRepoAsync(specifiedFilePath.ToString(), content);

        SetupGetDefaultChannelsAsync(repository, branch, channel, enabled: true);

        var options = CreateDefaultChannelStatusOptions(
            repository: repository,
            branch: branch,
            channel: channel,
            disable: true,
            configurationBranch: testBranch,
            configurationFilePath: specifiedFilePath.ToString());
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the default channel was updated
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, specifiedFilePath.ToString());
        File.Exists(fullPath).Should().BeTrue("File should still exist");

        var defaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        defaultChannels.Should().HaveCount(1);
        defaultChannels[0].Enabled.Should().BeFalse();
    }

    [Test]
    public async Task DefaultChannelStatusOperation_WithConfigRepo_FailsWhenAttemptingToEnableAlreadyEnabled()
    {
        // Arrange
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "main";
        var channel = "test-channel";
        var testBranch = GetTestBranch();

        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "dotnet-test-repo.yml";
        var existingContent = $"""
            - Repository: {repository}
              Branch: {branch}
              Channel: {channel}
              Enabled: true
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupGetDefaultChannelsAsync(repository, branch, channel, enabled: true);

        var options = CreateDefaultChannelStatusOptions(
            repository: repository,
            branch: branch,
            channel: channel,
            enable: true,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);
    }

    [Test]
    public async Task DefaultChannelStatusOperation_WithConfigRepo_FailsWhenAttemptingToDisableAlreadyDisabled()
    {
        // Arrange
        var repository = "https://github.com/dotnet/test-repo";
        var branch = "main";
        var channel = "test-channel";
        var testBranch = GetTestBranch();

        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / "dotnet-test-repo.yml";
        var existingContent = $"""
            - Repository: {repository}
              Branch: {branch}
              Channel: {channel}
              Enabled: false
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupGetDefaultChannelsAsync(repository, branch, channel, enabled: false);

        var options = CreateDefaultChannelStatusOptions(
            repository: repository,
            branch: branch,
            channel: channel,
            disable: true,
            configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);
    }

    #region Helper methods

    private void SetupGetDefaultChannelsAsync(string repository, string branch, string channel, bool enabled, int id = 1)
    {
        var defaultChannel = new DefaultChannel(
            id: id,
            repository: repository,
            enabled: enabled)
        {
            Branch = branch,
            Channel = new Channel(1, channel, "test")
        };

        BarClientMock
            .Setup(x => x.GetDefaultChannelsAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new List<DefaultChannel> { defaultChannel });
    }

    private DefaultChannelStatusCommandLineOptions CreateDefaultChannelStatusOptions(
        string? repository = null,
        string? branch = null,
        string? channel = null,
        int id = -1,
        bool enable = false,
        bool disable = false,
        string? configurationBranch = null,
        string configurationBaseBranch = DefaultBranch,
        string? configurationFilePath = null,
        bool noPr = true)
    {
        return new DefaultChannelStatusCommandLineOptions
        {
            Repository = repository,
            Branch = branch,
            Channel = channel,
            Id = id,
            Enable = enable,
            Disable = disable,
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = configurationBranch,
            ConfigurationBaseBranch = configurationBaseBranch,
            ConfigurationFilePath = configurationFilePath,
            NoPr = noPr
        };
    }

    private DefaultChannelStatusOperation CreateOperation(DefaultChannelStatusCommandLineOptions options)
    {
        return new DefaultChannelStatusOperation(
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

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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class AddDefaultChannelOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<AddDefaultChannelOperation>> _loggerMock = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<AddDefaultChannelOperation>>();
    }

    [Test]
    public async Task AddDefaultChannelOperation_WithConfigRepo_CreatesDefaultChannelFile()
    {
        // Arrange - Define expected default channel first
        var expectedDefaultChannel = new DefaultChannelYaml
        {
            Repository = "https://github.com/dotnet/test-repo",
            Branch = "main",
            Channel = "test-channel",
            Enabled = true
        };
        var testBranch = GetTestBranch();
        var expectedFilePath = ConfigFilePathResolver.GetDefaultDefaultChannelFilePath(expectedDefaultChannel);

        SetupChannel(expectedDefaultChannel.Channel);

        var options = CreateAddDefaultChannelOptions(expectedDefaultChannel, configurationBranch: testBranch);
        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify file was created at the expected path
        await CheckoutBranch(testBranch);
        var fullExpectedPath = Path.Combine(ConfigurationRepoPath, expectedFilePath.ToString());
        File.Exists(fullExpectedPath).Should().BeTrue($"Expected file at {fullExpectedPath}");

        // Deserialize and verify default channel properties
        var defaultChannels = await DeserializeDefaultChannelsAsync(fullExpectedPath);
        defaultChannels.Should().HaveCount(1);

        var actualDefaultChannel = defaultChannels[0];
        actualDefaultChannel.Repository.Should().Be(expectedDefaultChannel.Repository);
        actualDefaultChannel.Branch.Should().Be(expectedDefaultChannel.Branch);
        actualDefaultChannel.Channel.Should().Be(expectedDefaultChannel.Channel);
        actualDefaultChannel.Enabled.Should().Be(expectedDefaultChannel.Enabled);
    }

    [Test]
    public async Task AddDefaultChannelOperation_WithConfigRepo_AppendsToExistingFile()
    {
        // Arrange - Define expected default channel first
        var expectedDefaultChannel = new DefaultChannelYaml
        {
            Repository = "https://github.com/dotnet/test-repo",
            Branch = "release/8.0",
            Channel = "test-channel",
            Enabled = true
        };
        var testBranch = GetTestBranch();

        const string configFileName = "dotnet-test-repo.yml";

        // Create existing default channel file at the expected location
        var existingContent = """
            - Repository: https://github.com/dotnet/test-repo
              Branch: main
              Channel: existing-channel
              Enabled: true
            """;
        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / configFileName;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupChannel(expectedDefaultChannel.Channel);

        var options = CreateAddDefaultChannelOptions(
            expectedDefaultChannel,
            configurationBranch: testBranch);

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Deserialize and verify both default channels are present
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var defaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        defaultChannels.Should().HaveCount(2);

        defaultChannels.Should().Contain(dc => dc.Branch == "main");
        defaultChannels.Should().Contain(dc => dc.Branch == expectedDefaultChannel.Branch);
    }

    [Test]
    public async Task AddDefaultChannelOperation_WithConfigRepo_FailsWhenEquivalentDefaultChannelExistsInYamlFile()
    {
        // Arrange - Define the default channel we want to add
        var defaultChannelToAdd = new DefaultChannelYaml
        {
            Repository = "https://github.com/dotnet/test-repo",
            Branch = "main",
            Channel = "test-channel",
            Enabled = true
        };

        const string configFileName = "dotnet-test-repo.yml";

        // Create existing default channel file with an equivalent default channel (same repo, branch, channel)
        var existingContent = $"""
            - Repository: {defaultChannelToAdd.Repository}
              Branch: {defaultChannelToAdd.Branch}
              Channel: {defaultChannelToAdd.Channel}
              Enabled: true
            """;
        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / configFileName;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupChannel(defaultChannelToAdd.Channel);

        var options = CreateAddDefaultChannelOptions(
            defaultChannelToAdd,
            configurationBranch: GetTestBranch());

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);

        // Verify the file still only contains the original default channel
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var defaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        defaultChannels.Should().HaveCount(1);
        defaultChannels[0].Repository.Should().Be(defaultChannelToAdd.Repository);
    }

    private AddDefaultChannelCommandLineOptions CreateAddDefaultChannelOptions(
        DefaultChannelYaml defaultChannel,
        string? configurationBranch = null,
        string configurationBaseBranch = DefaultBranch,
        string? configurationFilePath = null,
        bool noPr = true)
    {
        return new AddDefaultChannelCommandLineOptions
        {
            Repository = defaultChannel.Repository,
            Branch = defaultChannel.Branch,
            Channel = defaultChannel.Channel,
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = configurationBranch,
            ConfigurationBaseBranch = configurationBaseBranch,
            ConfigurationFilePath = configurationFilePath,
            NoPr = noPr,
            NoConfirmation = true,
        };
    }

    private AddDefaultChannelOperation CreateOperation(AddDefaultChannelCommandLineOptions options)
    {
        return new AddDefaultChannelOperation(
            options,
            _loggerMock.Object,
            BarClientMock.Object,
            RemoteFactoryMock.Object,
            GitRepoFactory,
            ConfigurationRepositoryManager);
    }

    /// <summary>
    /// Deserializes a YAML file containing a list of default channels.
    /// </summary>
    private static async Task<List<DefaultChannelYaml>> DeserializeDefaultChannelsAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        return YamlDeserializer.Deserialize<List<DefaultChannelYaml>>(content) ?? [];
    }
}

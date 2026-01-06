// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
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
public class UpdateDefaultChannelOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<UpdateDefaultChannelOperation>> _loggerMock = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<UpdateDefaultChannelOperation>>();
    }

    protected override void SetupBarClientMock()
    {
        base.SetupBarClientMock();

        // Setup default channels response
        var defaultChannels = new List<DefaultChannel>
        {
            new DefaultChannel(
                1,
                "https://github.com/dotnet/test-repo",
                true)
            {
                Branch = "refs/heads/main",
                Channel = new Channel(1, "test-channel", "test")
            }
        };

        BarClientMock
            .Setup(x => x.GetDefaultChannelsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(defaultChannels);
    }

    [Test]
    public async Task UpdateDefaultChannelOperation_WithConfigRepo_UpdatesEnabledStatus()
    {
        // Arrange - Create an existing default channel
        var originalDefaultChannel = new DefaultChannelYaml
        {
            Repository = "https://github.com/dotnet/test-repo",
            Branch = "refs/heads/main",
            Channel = "test-channel",
            Enabled = true
        };
        var testBranch = GetTestBranch();
        var expectedFilePath = ConfigFilePathResolver.GetDefaultDefaultChannelFilePath(originalDefaultChannel);

        const string configFileName = "dotnet-test-repo.yml";
        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / configFileName;

        // Create existing default channel file
        var existingContent = """
            - Repository: https://github.com/dotnet/test-repo
              Branch: refs/heads/main
              Channel: test-channel
              Enabled: true
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupChannel("test-channel");

        var options = new UpdateDefaultChannelCommandLineOptions
        {
            Repository = "https://github.com/dotnet/test-repo",
            Branch = "main",
            Channel = "test-channel",
            Disable = true,
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = testBranch,
            ConfigurationBaseBranch = DefaultBranch,
            NoPr = true,
        };

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the file was updated
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var defaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        defaultChannels.Should().HaveCount(1);
        defaultChannels[0].Enabled.Should().BeFalse();
    }

    [Test]
    public async Task UpdateDefaultChannelOperation_WithConfigRepo_UpdatesChannel()
    {
        // Arrange - Create an existing default channel
        var testBranch = GetTestBranch();
        const string configFileName = "dotnet-test-repo.yml";
        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / configFileName;

        // Create existing default channel file
        var existingContent = """
            - Repository: https://github.com/dotnet/test-repo
              Branch: refs/heads/main
              Channel: old-channel
              Enabled: true
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        // Setup both channels
        SetupChannel("old-channel", 1);
        SetupChannel("new-channel", 2);

        // Setup the default channels list to include the channel we want to update
        var defaultChannels = new List<DefaultChannel>
        {
            new DefaultChannel(
                1,
                "https://github.com/dotnet/test-repo",
                true)
            {
                Branch = "refs/heads/main",
                Channel = new Channel(1, "old-channel", "test")
            }
        };

        BarClientMock
            .Setup(x => x.GetDefaultChannelsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(defaultChannels);

        var options = new UpdateDefaultChannelCommandLineOptions
        {
            Repository = "https://github.com/dotnet/test-repo",
            Branch = "main",
            Channel = "old-channel",
            NewChannel = "new-channel",
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = testBranch,
            ConfigurationBaseBranch = DefaultBranch,
            NoPr = true,
        };

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the file was updated with the new channel
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var updatedDefaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        updatedDefaultChannels.Should().HaveCount(1);
        updatedDefaultChannels[0].Channel.Should().Be("new-channel");
        updatedDefaultChannels[0].Repository.Should().Be("https://github.com/dotnet/test-repo");
        updatedDefaultChannels[0].Branch.Should().Be("refs/heads/main");
    }

    [Test]
    public async Task UpdateDefaultChannelOperation_WithConfigRepo_UpdatesBranch()
    {
        // Arrange - Create an existing default channel
        var testBranch = GetTestBranch();
        const string configFileName = "dotnet-test-repo.yml";
        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / configFileName;

        // Create existing default channel file
        var existingContent = """
            - Repository: https://github.com/dotnet/test-repo
              Branch: refs/heads/main
              Channel: test-channel
              Enabled: true
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        SetupChannel("test-channel");

        var options = new UpdateDefaultChannelCommandLineOptions
        {
            Repository = "https://github.com/dotnet/test-repo",
            Branch = "main",
            Channel = "test-channel",
            NewBranch = "release/8.0",
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = testBranch,
            ConfigurationBaseBranch = DefaultBranch,
            NoPr = true,
        };

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify the file was updated with the new branch
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var updatedDefaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        updatedDefaultChannels.Should().HaveCount(1);
        updatedDefaultChannels[0].Branch.Should().Be("refs/heads/release/8.0");
    }

    [Test]
    public async Task UpdateDefaultChannelOperation_WithConfigRepo_FailsWhenDefaultChannelNotFound()
    {
        // Arrange - Don't create any default channel file
        var testBranch = GetTestBranch();

        SetupChannel("test-channel");

        var options = new UpdateDefaultChannelCommandLineOptions
        {
            Repository = "https://github.com/dotnet/nonexistent-repo",
            Branch = "main",
            Channel = "test-channel",
            Disable = true,
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = testBranch,
            ConfigurationBaseBranch = DefaultBranch,
            NoPr = true,
        };

        // Setup BarClient to return no default channels
        BarClientMock
            .Setup(x => x.GetDefaultChannelsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(new List<DefaultChannel>());

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);
    }

    [Test]
    public async Task UpdateDefaultChannelOperation_WithConfigRepo_UpdatesMultipleProperties()
    {
        // Arrange - Create an existing default channel
        var testBranch = GetTestBranch();
        const string configFileName = "dotnet-test-repo.yml";
        var configFilePath = new UnixPath(ConfigFilePathResolver.DefaultChannelFolderPath) / configFileName;

        // Create existing default channel file
        var existingContent = """
            - Repository: https://github.com/dotnet/test-repo
              Branch: refs/heads/main
              Channel: old-channel
              Enabled: true
            """;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        // Setup both channels
        SetupChannel("old-channel", 1);
        SetupChannel("new-channel", 2);

        // Setup the default channels list
        var defaultChannels = new List<DefaultChannel>
        {
            new DefaultChannel(
                1,
                "https://github.com/dotnet/test-repo",
                true)
            {
                Branch = "refs/heads/main",
                Channel = new Channel(1, "old-channel", "test")
            }
        };

        BarClientMock
            .Setup(x => x.GetDefaultChannelsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(defaultChannels);

        var options = new UpdateDefaultChannelCommandLineOptions
        {
            Repository = "https://github.com/dotnet/test-repo",
            Branch = "main",
            Channel = "old-channel",
            NewChannel = "new-channel",
            NewBranch = "release/9.0",
            Disable = true,
            ConfigurationRepository = ConfigurationRepoPath,
            ConfigurationBranch = testBranch,
            ConfigurationBaseBranch = DefaultBranch,
            NoPr = true,
        };

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify all properties were updated
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var updatedDefaultChannels = await DeserializeDefaultChannelsAsync(fullPath);
        updatedDefaultChannels.Should().HaveCount(1);
        updatedDefaultChannels[0].Channel.Should().Be("new-channel");
        updatedDefaultChannels[0].Branch.Should().Be("refs/heads/release/9.0");
        updatedDefaultChannels[0].Enabled.Should().BeFalse();
    }

    private UpdateDefaultChannelOperation CreateOperation(UpdateDefaultChannelCommandLineOptions options)
    {
        return new UpdateDefaultChannelOperation(
            options,
            _loggerMock.Object,
            BarClientMock.Object,
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

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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class AddChannelOperationConfigRepoTests : ConfigurationManagementTestBase
{
    private Mock<ILogger<AddChannelOperation>> _loggerMock = null!;
    private List<ProductConstructionService.Client.Models.Channel> _channels = null!;

    [SetUp]
    public override async Task SetupAsync()
    {
        await base.SetupAsync();
        _loggerMock = new Mock<ILogger<AddChannelOperation>>();
        _channels = [];
    }

    protected override void SetupBarClientMock()
    {
        base.SetupBarClientMock();

        // Setup GetChannelsAsync to return the channels list
        BarClientMock
            .Setup(x => x.GetChannelsAsync())
            .ReturnsAsync(() => _channels);
    }

    private void SetupChannel(string channelName, int channelId, string classification = "test")
    {
        var channel = new ProductConstructionService.Client.Models.Channel(channelId, channelName, classification);
        _channels.Add(channel);
        
        BarClientMock
            .Setup(x => x.GetChannelAsync(channelName))
            .ReturnsAsync(channel);
    }

    [Test]
    public async Task AddChannelOperation_WithConfigRepo_CreatesChannelFile()
    {
        // Arrange
        var channelName = ".NET 11";
        var classification = "product";
        var testBranch = GetTestBranch();

        var expectedChannel = new ChannelYaml
        {
            Name = channelName,
            Classification = classification
        };

        var expectedFilePath = ConfigFilePathResolver.GetDefaultChannelFilePath(expectedChannel);

        var options = CreateAddChannelOptions(
            channelName,
            classification,
            configurationBranch: testBranch);

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
        actualChannel.Name.Should().Be(channelName);
        actualChannel.Classification.Should().Be(classification);
    }

    [Test]
    public async Task AddChannelOperation_WithConfigRepo_AppendsToExistingFile()
    {
        // Arrange
        var channelName = ".NET 11";
        var classification = "product";
        var testBranch = GetTestBranch();

        const string existingChannelName = ".NET 10";
        const string configFileName = ".net-10.yml";

        // Create existing channel file
        var existingContent = $"""
            - Name: {existingChannelName}
              Classification: product
            """;
        var configFilePath = new UnixPath(ConfigFilePathResolver.ChannelFolderPath) / configFileName;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        var options = CreateAddChannelOptions(
            channelName,
            classification,
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

        channels.Should().Contain(c => c.Name == existingChannelName);
        channels.Should().Contain(c => c.Name == channelName);
    }

    [Test]
    public async Task AddChannelOperation_WithConfigRepo_FailsWhenChannelAlreadyExistsInApi()
    {
        // Arrange
        var channelName = ".NET 11";
        var classification = "product";
        var testBranch = GetTestBranch();

        // Setup an existing channel in the API
        SetupChannel(channelName, channelId: 42);

        var options = CreateAddChannelOptions(
            channelName,
            classification,
            configurationBranch: testBranch);

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
    public async Task AddChannelOperation_WithConfigRepo_FailsWhenChannelAlreadyExistsInYamlFile()
    {
        // Arrange
        var channelName = ".NET 11";
        var classification = "product";
        var testBranch = GetTestBranch();

        const string configFileName = ".net-11.yml";

        // Create existing channel file with the same channel name
        var existingContent = $"""
            - Name: {channelName}
              Classification: product
            """;
        var configFilePath = new UnixPath(ConfigFilePathResolver.ChannelFolderPath) / configFileName;
        await CreateFileInConfigRepoAsync(configFilePath.ToString(), existingContent);

        var options = CreateAddChannelOptions(
            channelName,
            classification,
            configurationBranch: testBranch,
            configurationFilePath: configFilePath.ToString());

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.ErrorCode);

        // Verify the file still only contains the original channel
        await CheckoutBranch(testBranch);
        var fullPath = Path.Combine(ConfigurationRepoPath, configFilePath.ToString());
        var channels = await DeserializeChannelsAsync(fullPath);
        channels.Should().HaveCount(1);
        channels[0].Name.Should().Be(channelName);
    }

    [Test]
    public async Task AddChannelOperation_WithConfigRepo_FileContentIsValidYaml()
    {
        // Arrange
        var channelName = ".NET 11 Preview 5";
        var classification = "product";
        var testBranch = GetTestBranch();

        var expectedChannel = new ChannelYaml
        {
            Name = channelName,
            Classification = classification
        };

        var expectedFilePath = ConfigFilePathResolver.GetDefaultChannelFilePath(expectedChannel);

        var options = CreateAddChannelOptions(
            channelName,
            classification,
            configurationBranch: testBranch);

        var operation = CreateOperation(options);

        // Act
        int result = await operation.ExecuteAsync();

        // Assert
        result.Should().Be(Constants.SuccessCode);

        // Verify file was created at the expected path
        await CheckoutBranch(testBranch);
        var fullExpectedPath = Path.Combine(ConfigurationRepoPath, expectedFilePath);
        File.Exists(fullExpectedPath).Should().BeTrue($"Expected file at {fullExpectedPath}");

        // Deserialize and verify channel properties match expected values
        var channels = await DeserializeChannelsAsync(fullExpectedPath);
        channels.Should().HaveCount(1);

        var actualChannel = channels[0];
        actualChannel.Name.Should().Be(channelName);
        actualChannel.Classification.Should().Be(classification);
    }

    private AddChannelCommandLineOptions CreateAddChannelOptions(
        string name,
        string classification,
        string? configurationBranch = null,
        string configurationBaseBranch = DefaultBranch,
        string? configurationFilePath = null,
        bool noPr = true)
    {
        return new AddChannelCommandLineOptions
        {
            Name = name,
            Classification = classification,
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

    /// <summary>
    /// Deserializes a YAML file containing a list of channels.
    /// </summary>
    private static async Task<List<ChannelYaml>> DeserializeChannelsAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        return YamlDeserializer.Deserialize<List<ChannelYaml>>(content) ?? [];
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;

namespace Microsoft.DotNet.MaestroConfiguration.Client.Tests;

public class ConfigFilePathResolverTests
{
    [Theory]
    [InlineData("https://dev.azure.com/dnceng/internal/_git/dotnet-aspnetcore", "configuration/subscriptions/dotnet-aspnetcore.yml")]
    [InlineData("https://github.com/dotnet/aspnetcore", "configuration/subscriptions/dotnet-aspnetcore.yml")]
    [InlineData("https://dev.azure.com/devdiv/DevDiv/_git/vs-code-coverage", "configuration/subscriptions/vs-code-coverage.yml")]
    [InlineData("https://dev.azure.com/microsoft/DWriteCore/_git/DWriteCore", "configuration/subscriptions/DWriteCore.yml")]
    public void GetDefaultSubscriptionFilePath_ReturnsExpectedPath(string targetRepository, string expectedPath)
    {
        // Arrange
        SubscriptionYaml subscription = new()
        {
            Id = Guid.NewGuid(),
            Channel = "test-channel",
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = targetRepository,
            TargetBranch = "main"
        };

        // Act
        var result = ConfigFilePathResolver.GetDefaultSubscriptionFilePath(subscription);

        // Assert
        NormalizeString(expectedPath).Should().Be(NormalizeString(result));
    }

    [Theory]
    [InlineData(".NET 10", "configuration/channels/.net-10.yml")]
    [InlineData(".NET 11 Preview 1", "configuration/channels/.net-11.yml")]
    [InlineData(".NET 9 Eng - Validation", "configuration/channels/.net-9.yml")]
    [InlineData("VS 18.6", "configuration/channels/vs.yml")]
    [InlineData("WindowsAppSDK-Nightly", "configuration/channels/windows.yml")]
    [InlineData("Test_Channel_123", "configuration/channels/test.yml")]
    [InlineData("My-Channel.Name", "configuration/channels/other.yml")]
    public void GetDefaultChannelFilePath_ReturnsExpectedPath(string channelName, string expectedPath)
    {
        ChannelYaml channel = new()
        {
            Name = channelName,
            Classification = "test-classification"
        };

        var result = ConfigFilePathResolver.GetDefaultChannelFilePath(channel);

        NormalizeString(expectedPath).Should().Be(NormalizeString(result));
    }

    private string NormalizeString(string path)
    {
        return path.Replace("\\", "/");
    }
}

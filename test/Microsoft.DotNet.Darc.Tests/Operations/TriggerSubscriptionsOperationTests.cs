// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
public class TriggerSubscriptionsOperationTests
{
    [Test]
    public async Task ExecuteAsync_WithOnlyDisabledSubscriptions_ShowsImprovedMessage()
    {
        // Arrange
        var mockBarClient = new Mock<IBarApiClient>();
        var logger = new NullLogger<TriggerSubscriptionsOperation>();

        var disabledSubscription = new Subscription(
            Guid.Parse("dec3dc5c-371d-4fe1-4538-08d7d6786c95"),
            false, // enabled
            false, // sourceEnabled
            "https://dev.azure.com/dnceng/internal/_git/dotnet-core-setup",
            "https://dev.azure.com/dnceng/internal/_git/dotnet-diagnostictests",
            "release/3.1",
            pullRequestFailureNotificationTags: null,
            sourceDirectory: null,
            targetDirectory: null,
            excludedAssets: new List<string>())
        {
            Channel = new Channel(1, ".NET Core 3.1 Internal Servicing", "test"),
            Policy = new SubscriptionPolicy(false, UpdateFrequency.EveryDay)
        };

        mockBarClient.Setup(x => x.GetSubscriptionsAsync(null, null, null))
                    .ReturnsAsync(new[] { disabledSubscription });

        mockBarClient.Setup(x => x.GetDefaultChannelsAsync(null, null, null))
                    .ReturnsAsync(Array.Empty<DefaultChannel>());

        var options = new TriggerSubscriptionsCommandLineOptions
        {
            SourceRepository = "core-setup",
            TargetRepository = "diagnostictests",
            Channel = ".NET Core 3.1 Internal Servicing",
            NoConfirmation = true
        };

        var operation = new TriggerSubscriptionsOperation(options, mockBarClient.Object, logger);

        // Capture console output
        using var stringWriter = new StringWriter();
        var originalConsoleOut = Console.Out;
        Console.SetOut(stringWriter);

        try
        {
            // Act
            int result = await operation.ExecuteAsync();

            // Assert
            result.Should().Be(Constants.ErrorCode);
            
            var output = stringWriter.ToString();
            output.Should().Contain("The following 1 subscription(s) are disabled and will not be triggered");
            output.Should().Contain("All matching subscriptions are disabled. No subscriptions can be triggered.");
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
        }
    }

    [Test]
    public async Task ExecuteAsync_WithNoMatchingSubscriptions_ShowsOriginalMessage()
    {
        // Arrange
        var mockBarClient = new Mock<IBarApiClient>();
        var logger = new NullLogger<TriggerSubscriptionsOperation>();

        mockBarClient.Setup(x => x.GetSubscriptionsAsync(null, null, null))
                    .ReturnsAsync(Array.Empty<Subscription>());

        mockBarClient.Setup(x => x.GetDefaultChannelsAsync(null, null, null))
                    .ReturnsAsync(Array.Empty<DefaultChannel>());

        var options = new TriggerSubscriptionsCommandLineOptions
        {
            SourceRepository = "non-existent-repo",
            TargetRepository = "another-non-existent-repo",
            NoConfirmation = true
        };

        var operation = new TriggerSubscriptionsOperation(options, mockBarClient.Object, logger);

        // Capture console output
        using var stringWriter = new StringWriter();
        var originalConsoleOut = Console.Out;
        Console.SetOut(stringWriter);

        try
        {
            // Act
            int result = await operation.ExecuteAsync();

            // Assert
            result.Should().Be(Constants.ErrorCode);
            
            var output = stringWriter.ToString();
            output.Should().Contain("No subscriptions found matching the specified criteria.");
            output.Should().NotContain("All matching subscriptions are disabled");
        }
        finally
        {
            Console.SetOut(originalConsoleOut);
        }
    }
}
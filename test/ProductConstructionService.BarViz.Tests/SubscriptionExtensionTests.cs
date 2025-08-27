// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;

namespace ProductConstructionService.BarViz.Tests;

[TestFixture]
public class SubscriptionExtensionTests
{
    private Subscription CreateTestSubscription(
        bool sourceEnabled = false, 
        string? sourceDirectory = null,
        string? targetDirectory = null)
    {
        return new Subscription(
            Guid.NewGuid(),
            true, // enabled
            sourceEnabled,
            "https://github.com/dotnet/runtime",
            "https://github.com/dotnet/aspnetcore", 
            "main",
            sourceDirectory ?? string.Empty,
            targetDirectory ?? string.Empty,
            string.Empty,
            new List<string>())
        {
            Channel = new Channel(1, ".NET 9", "test")
        };
    }

    [Test]
    public void IsBackflow_ReturnsTrue_WhenSourceEnabledAndSourceDirectoryNotEmpty()
    {
        var subscription = CreateTestSubscription(sourceEnabled: true, sourceDirectory: "src/runtime");
        
        subscription.IsBackflow().Should().BeTrue();
    }

    [Test]
    public void IsBackflow_ReturnsFalse_WhenSourceEnabledButSourceDirectoryEmpty()
    {
        var subscription = CreateTestSubscription(sourceEnabled: true, sourceDirectory: "");
        
        subscription.IsBackflow().Should().BeFalse();
    }

    [Test]
    public void IsBackflow_ReturnsFalse_WhenNotSourceEnabled()
    {
        var subscription = CreateTestSubscription(sourceEnabled: false, sourceDirectory: "src/runtime");
        
        subscription.IsBackflow().Should().BeFalse();
    }

    [Test]
    public void IsForwardFlow_ReturnsTrue_WhenSourceEnabledAndTargetDirectoryNotEmpty()
    {
        var subscription = CreateTestSubscription(sourceEnabled: true, targetDirectory: "src/aspnetcore");
        
        subscription.IsForwardFlow().Should().BeTrue();
    }

    [Test]
    public void IsForwardFlow_ReturnsFalse_WhenSourceEnabledButTargetDirectoryEmpty()
    {
        var subscription = CreateTestSubscription(sourceEnabled: true, targetDirectory: "");
        
        subscription.IsForwardFlow().Should().BeFalse();
    }

    [Test]
    public void IsForwardFlow_ReturnsFalse_WhenNotSourceEnabled()
    {
        var subscription = CreateTestSubscription(sourceEnabled: false, targetDirectory: "src/aspnetcore");
        
        subscription.IsForwardFlow().Should().BeFalse();
    }
}
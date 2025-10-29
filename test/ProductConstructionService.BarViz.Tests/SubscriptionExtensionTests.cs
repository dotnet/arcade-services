// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;

namespace ProductConstructionService.BarViz.Tests;

[TestFixture]
public class SubscriptionExtensionTests
{
    private static Subscription CreateTestSubscription(
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
            [])
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

    // Test helper method to demonstrate the new filtering logic
    private static string ExpandPartialFilter(string filter)
    {
        if (!filter.StartsWith(':'))
            return filter;

        var availableFilters = new[] { ":codeflow", ":disabled", ":haspr", ":ff", ":forwardflow", ":bf", ":backflow" };
        var matches = availableFilters.Where(f => f.StartsWith(filter)).ToList();
        
        // Only expand if there's exactly one match
        return matches.Count == 1 ? matches[0] : filter;
    }

    [TestCase(":c", ":codeflow", Description = "Partial match for codeflow")]
    [TestCase(":d", ":disabled", Description = "Partial match for disabled")]
    [TestCase(":h", ":haspr", Description = "Partial match for haspr")]
    [TestCase(":f", ":f", Description = "Ambiguous partial match (ff vs forwardflow) - no expansion")]
    [TestCase(":ff", ":ff", Description = "Exact match for forward flow short form")]
    [TestCase(":forward", ":forwardflow", Description = "Partial match for forwardflow")]
    [TestCase(":b", ":b", Description = "Ambiguous partial match (bf vs backflow) - no expansion")]
    [TestCase(":bf", ":bf", Description = "Exact match for backflow short form")]
    [TestCase(":back", ":backflow", Description = "Partial match for backflow")]
    [TestCase(":xyz", ":xyz", Description = "No match - return original")]
    public void ExpandPartialFilter_WorksCorrectly(string input, string expected)
    {
        ExpandPartialFilter(input).Should().Be(expected);
    }

    [Test]
    public void MockUIFilteringScenarios_DemonstrateNewFunctionality()
    {
        // Create test subscriptions to demonstrate the new filters
        var forwardFlowSubscription = CreateTestSubscription(
            sourceEnabled: true, 
            targetDirectory: "src/aspnetcore",
            sourceDirectory: "");

        var backflowSubscription = CreateTestSubscription(
            sourceEnabled: true, 
            sourceDirectory: "src/runtime",
            targetDirectory: "");

        var regularCodeflowSubscription = CreateTestSubscription(
            sourceEnabled: true,
            sourceDirectory: "",
            targetDirectory: "");

        var nonCodeflowSubscription = CreateTestSubscription(
            sourceEnabled: false,
            sourceDirectory: "",
            targetDirectory: "");

        var subscriptions = new[] { forwardFlowSubscription, backflowSubscription, regularCodeflowSubscription, nonCodeflowSubscription };

        // Test new :ff filter
        var forwardFlowResults = subscriptions.Where(s => MockIsMatch(s, ":ff")).ToList();
        forwardFlowResults.Should().ContainSingle()
            .Which.Should().Be(forwardFlowSubscription, "Only forward flow subscription should match :ff");

        // Test new :bf filter  
        var backflowResults = subscriptions.Where(s => MockIsMatch(s, ":bf")).ToList();
        backflowResults.Should().ContainSingle()
            .Which.Should().Be(backflowSubscription, "Only backflow subscription should match :bf");

        // Test new :forwardflow filter
        var forwardFlowFullResults = subscriptions.Where(s => MockIsMatch(s, ":forwardflow")).ToList();
        forwardFlowFullResults.Should().ContainSingle()
            .Which.Should().Be(forwardFlowSubscription, "Only forward flow subscription should match :forwardflow");

        // Test new :backflow filter  
        var backflowFullResults = subscriptions.Where(s => MockIsMatch(s, ":backflow")).ToList();
        backflowFullResults.Should().ContainSingle()
            .Which.Should().Be(backflowSubscription, "Only backflow subscription should match :backflow");

        // Test partial matching - :forward should expand to :forwardflow
        var partialForwardResults = subscriptions.Where(s => MockIsMatch(s, ":forward")).ToList();
        partialForwardResults.Should().ContainSingle()
            .Which.Should().Be(forwardFlowSubscription, "Partial match :forward should expand to :forwardflow");

        // Test partial matching - :back should expand to :backflow
        var partialBackResults = subscriptions.Where(s => MockIsMatch(s, ":back")).ToList();
        partialBackResults.Should().ContainSingle()
            .Which.Should().Be(backflowSubscription, "Partial match :back should expand to :backflow");

        // Test existing :codeflow filter still works
        var codeflowResults = subscriptions.Where(s => MockIsMatch(s, ":codeflow")).ToList();
        codeflowResults.Should().HaveCount(3, "Three source-enabled subscriptions should match :codeflow")
            .And.NotContain(nonCodeflowSubscription);
    }

    // Mock version of the IsMatch method from Subscriptions.razor
    private static bool MockIsMatch(Subscription subscription, string filter)
    {
        // Check for partial matches first (Goal 2)
        var trimmedFilter = filter.Trim().ToLowerInvariant();
        var expandedFilter = ExpandPartialFilter(trimmedFilter);
        if (expandedFilter != trimmedFilter)
        {
            trimmedFilter = expandedFilter;
        }

        return trimmedFilter switch
        {
            ":codeflow" => subscription.SourceEnabled,
            ":disabled" => !subscription.Enabled,
            ":haspr" => false, // Mock - no PRs for simplicity
            ":ff" or ":forwardflow" => subscription.IsForwardFlow(),
            ":bf" or ":backflow" => subscription.IsBackflow(),
            _ => false // Simplified for this test
        };
    }
}

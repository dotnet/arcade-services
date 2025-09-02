// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Moq;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Models.Darc.UnitTests;

/// <summary>
/// Tests for DependencyFlowEdge constructor ensuring correct assignment of From/To/Subscription
/// and default initialization of flags.
/// </summary>
public class DependencyFlowEdgeTests
{
    /// <summary>
    /// Validates that the constructor assigns the provided node references and subscription,
    /// and initializes BackEdge and OnLongestBuildPath to false while leaving PartOfCycle as null.
    /// Inputs:
    ///  - from and to nodes (optionally the same instance when sameFromTo is true).
    ///  - a valid Subscription instance.
    /// Expected:
    ///  - From/To/Subscription fields reference the exact provided instances.
    ///  - BackEdge == false, OnLongestBuildPath == false.
    ///  - PartOfCycle == null and IsToolingOnly == false immediately after construction.
    /// </summary>
    /// <param name="sameFromTo">If true, uses the same node instance for both From and To.</param>
    [Test]
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_AssignsReferencesAndInitializesDefaults(bool sameFromTo)
    {
        // Arrange
        var fromNode = CreateNode("https://repo/from", "main", "from-id");
        var toNode = sameFromTo ? fromNode : CreateNode("https://repo/to", "release", "to-id");
        var subscription = CreateSubscription(
            id: Guid.NewGuid(),
            enabled: true,
            sourceEnabled: true,
            sourceRepository: "https://src/repo",
            targetRepository: "https://tgt/repo",
            targetBranch: "main",
            sourceDirectory: "src",
            targetDirectory: "tgt",
            pullRequestFailureNotificationTags: "@team",
            excludedAssets: new List<string> { "asset1", "asset2" });

        // Act
        var edge = new DependencyFlowEdge(fromNode, toNode, subscription);

        // Assert
        edge.From.Should().BeSameAs(fromNode);
        edge.To.Should().BeSameAs(toNode);
        edge.Subscription.Should().BeSameAs(subscription);

        edge.BackEdge.Should().BeFalse();
        edge.OnLongestBuildPath.Should().BeFalse();

        edge.PartOfCycle.Should().BeNull();
        edge.IsToolingOnly.Should().BeFalse();
    }

    private static DependencyFlowNode CreateNode(string repository, string branch, string id)
        => new DependencyFlowNode(repository, branch, id);

    private static Subscription CreateSubscription(
        Guid id,
        bool enabled,
        bool sourceEnabled,
        string sourceRepository,
        string targetRepository,
        string targetBranch,
        string sourceDirectory,
        string targetDirectory,
        string pullRequestFailureNotificationTags,
        List<string> excludedAssets)
        => new Subscription(
            id: id,
            enabled: enabled,
            sourceEnabled: sourceEnabled,
            sourceRepository: sourceRepository,
            targetRepository: targetRepository,
            targetBranch: targetBranch,
            sourceDirectory: sourceDirectory,
            targetDirectory: targetDirectory,
            pullRequestFailureNotificationTags: pullRequestFailureNotificationTags,
            excludedAssets: excludedAssets);
}

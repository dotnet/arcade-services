// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Moq;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.DarcLib.Tests.Models.Darc;


public class DependencyFlowNodeTests
{
    [Test]
    public void VerifyPathTimeIsEqualToOfficialBuildTimeWhenThereAreNoOutgoingEdges()
    {
        var node = new DependencyFlowNode("test", "test", Guid.NewGuid().ToString())
        {
            OfficialBuildTime = 100.0,
            OutgoingEdges = []
        };

        node.CalculateLongestPathTime();

        node.WorstCasePathTime.Should().Be(node.OfficialBuildTime);
        node.BestCasePathTime.Should().Be(node.OfficialBuildTime);
    }

    [Test]
    public void VerifyPathTimeIsCorrectForOutgoingProductNodes()
    {
        var node = new DependencyFlowNode("test", "test", Guid.NewGuid().ToString())
        {
            OfficialBuildTime = 100.0,
        };

        var edge1 = AddEdge(node, worstCasePathTime: 10, bestCasePathTime: 5, prBuildTime: 1, isToolingOnlyEdge: false);
        var edge2 = AddEdge(node, worstCasePathTime: 20, bestCasePathTime: 2, prBuildTime: 1, isToolingOnlyEdge: false);

        node.CalculateLongestPathTime();

        node.WorstCasePathTime.Should().Be(node.OfficialBuildTime + edge2.To.WorstCasePathTime + edge2.To.PrBuildTime);
        node.BestCasePathTime.Should().Be(node.OfficialBuildTime + edge1.To.BestCasePathTime);
    }

    [Test]
    public void VerifyPathTimeCalculationIgnoresToolingNodes()
    {
        var node = new DependencyFlowNode("test", "test", Guid.NewGuid().ToString())
        {
            OfficialBuildTime = 100.0,
        };

        var edge1 = AddEdge(node, worstCasePathTime: 10, bestCasePathTime: 5, prBuildTime: 1, isToolingOnlyEdge: false);
        var edge2 = AddEdge(node, worstCasePathTime: 20, bestCasePathTime: 2, prBuildTime: 1, isToolingOnlyEdge: false);
        AddEdge(node, worstCasePathTime: 30, bestCasePathTime: 20, prBuildTime: 1, isToolingOnlyEdge: true);

        node.CalculateLongestPathTime();

        node.WorstCasePathTime.Should().Be(node.OfficialBuildTime + edge2.To.WorstCasePathTime + edge2.To.PrBuildTime);
        node.BestCasePathTime.Should().Be(node.OfficialBuildTime + edge1.To.BestCasePathTime);
    }

    private static DependencyFlowEdge AddEdge(
        DependencyFlowNode fromNode,
        double worstCasePathTime,
        double bestCasePathTime,
        double prBuildTime,
        bool isToolingOnlyEdge)
    {
        var toNode = new DependencyFlowNode("test", "test", Guid.NewGuid().ToString())
        {
            BestCasePathTime = bestCasePathTime,
            PrBuildTime = prBuildTime,
            WorstCasePathTime = worstCasePathTime,
        };

        var subscription = new Subscription(
            Guid.NewGuid(),
            true,
            false,
            "source",
            "target",
            "test",
            sourceDirectory: null,
            targetDirectory: null,
            pullRequestFailureNotificationTags: string.Empty,
            excludedAssets: [])
        {
            LastAppliedBuild = new Build(
            id: 1,
            dateProduced: DateTimeOffset.Now,
            staleness: 0,
            released: false,
            stable: true,
            commit: "7a7e5c82abd287262a6efaf29902bb84a7bd81af",
            channels: [],
            assets: [],
            dependencies: [new(buildId: 1, isProduct: !isToolingOnlyEdge, timeToInclusionInMinutes: 1)],
            incoherencies: [])
        };

        var edge = new DependencyFlowEdge(fromNode, toNode, subscription)
        {
            IsToolingOnly = isToolingOnlyEdge
        };

        fromNode.OutgoingEdges.Add(edge);
        return edge;
    }

    /// <summary>
    /// Verifies that the constructor assigns the provided string parameters to the corresponding readonly fields.
    /// Inputs exercise typical and edge-case strings (empty, whitespace, long, and special-character-rich).
    /// Expected: The Repository, Branch, and Id fields equal the supplied values exactly (no normalization or validation).
    /// </summary>
    [TestCaseSource(nameof(Constructor_AssignsFields_FromParameters_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_AssignsFields_FromParameters(string repository, string branch, string id)
    {
        // Arrange
        // (Inputs supplied by TestCaseSource)

        // Act
        var node = new DependencyFlowNode(repository, branch, id);

        // Assert
        node.Repository.Should().Be(repository);
        node.Branch.Should().Be(branch);
        node.Id.Should().Be(id);
    }

    /// <summary>
    /// Verifies that the constructor initializes OutputChannels and InputChannels as non-null, empty HashSets
    /// using case-insensitive comparison (OrdinalIgnoreCase).
    /// Inputs: Simple non-empty strings for constructor parameters.
    /// Expected: Both sets start empty, treat different-cased strings as equal, and contain lookups are case-insensitive.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_CreatesCaseInsensitiveChannelSets()
    {
        // Arrange
        var node = new DependencyFlowNode("repo", "branch", "id");

        // Act
        node.OutputChannels.Should().NotBeNull();
        node.InputChannels.Should().NotBeNull();

        node.OutputChannels.Count.Should().Be(0);
        node.InputChannels.Count.Should().Be(0);

        node.OutputChannels.Add("Channel");
        node.OutputChannels.Add("CHANNEL");
        node.InputChannels.Add("Feed");
        node.InputChannels.Add("feed");

        // Assert
        node.OutputChannels.Count.Should().Be(1);
        node.InputChannels.Count.Should().Be(1);

        node.OutputChannels.Contains("channel").Should().BeTrue();
        node.InputChannels.Contains("FEED").Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the constructor initializes edge collections as empty lists and path/flag properties to defaults.
    /// Inputs: Simple non-empty strings for constructor parameters.
    /// Expected: OutgoingEdges/IncomingEdges are non-null and empty; WorstCasePathTime and BestCasePathTime are 0; OnLongestBuildPath is false.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_InitializesEmptyEdgesAndDefaultState()
    {
        // Arrange
        var node = new DependencyFlowNode("repo", "branch", "id");

        // Act
        // (No actions needed; we're validating post-constructor state)

        // Assert
        node.OutgoingEdges.Should().NotBeNull();
        node.IncomingEdges.Should().NotBeNull();
        node.OutgoingEdges.Count.Should().Be(0);
        node.IncomingEdges.Count.Should().Be(0);

        node.WorstCasePathTime.Should().Be(0d);
        node.BestCasePathTime.Should().Be(0d);
        node.OnLongestBuildPath.Should().BeFalse();
    }

    private static IEnumerable Constructor_AssignsFields_FromParameters_Cases()
    {
        yield return new TestCaseData(string.Empty, string.Empty, string.Empty).SetName("EmptyStrings");
        yield return new TestCaseData(" ", "\t\r\n", "a").SetName("WhitespaceStrings");
        yield return new TestCaseData(new string('r', 1024), "feature/XYZ", "12345678-ABCD-efgh-ijkl-9876543210zz").SetName("LongAndPathLikeStrings");
        yield return new TestCaseData("Repo/Name:~!@#$%^&*()_+", "release/*", "id:with:symbols|<>?\"\\").SetName("SpecialCharacterStrings");
    }

    /// <summary>
    /// Provides parameterized scenarios for IsToolingOnly:
    /// - Empty edges -> true (LINQ All returns true for empty sequences).
    /// - All edges tooling -> true.
    /// - Any non-tooling edge -> false.
    /// </summary>
    public static IEnumerable<TestCaseData> IsToolingOnlyCases()
    {
        yield return new TestCaseData(new bool[] { }, true).SetName("IsToolingOnly_NoOutgoingEdges_ReturnsTrue");
        yield return new TestCaseData(new[] { true }, true).SetName("IsToolingOnly_SingleToolingEdge_ReturnsTrue");
        yield return new TestCaseData(new[] { false }, false).SetName("IsToolingOnly_SingleNonToolingEdge_ReturnsFalse");
        yield return new TestCaseData(new[] { true, true }, true).SetName("IsToolingOnly_AllToolingEdges_ReturnsTrue");
        yield return new TestCaseData(new[] { true, false }, false).SetName("IsToolingOnly_MixedEdges_ReturnsFalse");
        yield return new TestCaseData(new[] { false, false }, false).SetName("IsToolingOnly_AllNonToolingEdges_ReturnsFalse");
    }

    /// <summary>
    /// Validates DependencyFlowNode.IsToolingOnly against various combinations of outgoing edges.
    /// Inputs:
    /// - edgeToolingFlags: sequence of flags indicating whether each outgoing edge is tooling-only.
    /// Expected:
    /// - Property returns true only when all edges are tooling-only (and also true when there are no edges).
    /// </summary>
    [TestCaseSource(nameof(IsToolingOnlyCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void IsToolingOnly_OutgoingEdgesToolingCombinations_ReturnsExpected(bool[] edgeToolingFlags, bool expected)
    {
        // Arrange
        var node = new DependencyFlowNode("repo", "branch", Guid.NewGuid().ToString());

        foreach (var isTooling in edgeToolingFlags)
        {
            var toNode = new DependencyFlowNode("repo-to", "branch-to", Guid.NewGuid().ToString());
            var subscription = new Subscription(
                Guid.NewGuid(),
                enabled: true,
                batchable: false,
                sourceRepository: "source",
                targetRepository: "target",
                targetBranch: "test",
                sourceDirectory: null,
                targetDirectory: null,
                pullRequestFailureNotificationTags: string.Empty,
                excludedAssets: [])
            {
                LastAppliedBuild = new Build(
                    id: 1,
                    dateProduced: DateTimeOffset.Now,
                    staleness: 0,
                    released: false,
                    stable: true,
                    commit: "7a7e5c82abd287262a6efaf29902bb84a7bd81af",
                    channels: [],
                    assets: [],
                    dependencies: [new BuildDependency(buildId: 1, isProduct: !isTooling, timeToInclusionInMinutes: 1)],
                    incoherencies: [])
            };

            var edge = new DependencyFlowEdge(node, toNode, subscription)
            {
                IsToolingOnly = isTooling
            };

            node.OutgoingEdges.Add(edge);
        }

        // Act
        var result = node.IsToolingOnly;

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that when a node has no outgoing edges, both WorstCasePathTime and BestCasePathTime
    /// are set to the node's OfficialBuildTime.
    /// Inputs:
    /// - No outgoing edges.
    /// - Varying official build times (including zero and negative).
    /// Expected:
    /// - WorstCasePathTime == OfficialBuildTime.
    /// - BestCasePathTime == OfficialBuildTime.
    /// </summary>
    [TestCase(0)]
    [TestCase(123.45)]
    [TestCase(-7.0)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CalculateLongestPathTime_NoOutgoingEdges_SetsTimesToOfficial(double officialBuildTime)
    {
        // Arrange
        var node = new DependencyFlowNode("repo", "branch", Guid.NewGuid().ToString())
        {
            OfficialBuildTime = officialBuildTime,
            OutgoingEdges = new List<DependencyFlowEdge>()
        };

        // Act
        node.CalculateLongestPathTime();

        // Assert
        node.WorstCasePathTime.Should().Be(officialBuildTime);
        node.BestCasePathTime.Should().Be(officialBuildTime);
    }

    /// <summary>
    /// Verifies that with product edges (non-tooling) present, the calculation:
    /// - Uses the maximum of (child.WorstCasePathTime + child.PrBuildTime) plus this node's OfficialBuildTime for WorstCasePathTime.
    /// - Uses the maximum of child.BestCasePathTime plus this node's OfficialBuildTime for BestCasePathTime.
    /// Inputs:
    /// - Two non-tooling edges with different worst/best/pr build times.
    /// Expected:
    /// - WorstCasePathTime computed using the edge with the larger (WorstCase + PR) sum.
    /// - BestCasePathTime computed using the edge with the larger BestCase.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CalculateLongestPathTime_WithOutgoingProductNodes_ComputesWorstAndBestPaths()
    {
        // Arrange
        var node = new DependencyFlowNode("repo", "branch", Guid.NewGuid().ToString())
        {
            OfficialBuildTime = 100.0,
        };

        var edge1 = AddEdge(node, worstCasePathTime: 10, bestCasePathTime: 5, prBuildTime: 1, isToolingOnlyEdge: false);
        var edge2 = AddEdge(node, worstCasePathTime: 20, bestCasePathTime: 2, prBuildTime: 1, isToolingOnlyEdge: false);

        // Act
        node.CalculateLongestPathTime();

        // Assert
        node.WorstCasePathTime.Should().Be(100.0 + edge2.To.WorstCasePathTime + edge2.To.PrBuildTime);
        node.BestCasePathTime.Should().Be(100.0 + edge1.To.BestCasePathTime);
    }

    /// <summary>
    /// Verifies that tooling-only edges are excluded from the longest path calculation.
    /// Inputs:
    /// - Two non-tooling edges and one tooling-only edge with larger child times.
    /// Expected:
    /// - Tooling-only edge is ignored.
    /// - Worst and Best case times are computed only from non-tooling edges.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CalculateLongestPathTime_IgnoresToolingOnlyEdges_DerivesFromProductEdges()
    {
        // Arrange
        var node = new DependencyFlowNode("repo", "branch", Guid.NewGuid().ToString())
        {
            OfficialBuildTime = 100.0,
        };

        var edge1 = AddEdge(node, worstCasePathTime: 10, bestCasePathTime: 5, prBuildTime: 1, isToolingOnlyEdge: false);
        var edge2 = AddEdge(node, worstCasePathTime: 20, bestCasePathTime: 2, prBuildTime: 1, isToolingOnlyEdge: false);
        AddEdge(node, worstCasePathTime: 30, bestCasePathTime: 20, prBuildTime: 1, isToolingOnlyEdge: true);

        // Act
        node.CalculateLongestPathTime();

        // Assert
        node.WorstCasePathTime.Should().Be(100.0 + edge2.To.WorstCasePathTime + edge2.To.PrBuildTime);
        node.BestCasePathTime.Should().Be(100.0 + edge1.To.BestCasePathTime);
    }

    /// <summary>
    /// Verifies that when all edges are marked as back edges initially, the implementation falls back
    /// to using all outgoing edges, then still filters out tooling-only edges before computation.
    /// Inputs:
    /// - Two back edges: one non-tooling, one tooling-only.
    /// Expected:
    /// - Tooling-only back edge is ignored.
    /// - Times are computed from the remaining non-tooling back edge.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CalculateLongestPathTime_AllEdgesAreBackEdges_FallbackThenFilterTooling()
    {
        // Arrange
        var node = new DependencyFlowNode("repo", "branch", Guid.NewGuid().ToString())
        {
            OfficialBuildTime = 50.0,
        };

        var keepEdge = AddEdge(node, worstCasePathTime: 40, bestCasePathTime: 7, prBuildTime: 3, isToolingOnlyEdge: false, backEdge: true);
        AddEdge(node, worstCasePathTime: 100, bestCasePathTime: 80, prBuildTime: 5, isToolingOnlyEdge: true, backEdge: true);

        // Act
        node.CalculateLongestPathTime();

        // Assert
        node.WorstCasePathTime.Should().Be(50.0 + keepEdge.To.WorstCasePathTime + keepEdge.To.PrBuildTime);
        node.BestCasePathTime.Should().Be(50.0 + keepEdge.To.BestCasePathTime);
    }

    /// <summary>
    /// Verifies that existing path times are preserved if they are larger than newly computed values,
    /// due to the use of Math.Max in the implementation.
    /// Inputs:
    /// - Existing WorstCasePathTime and BestCasePathTime set higher than any edge-based computation.
    /// - Only low-time outgoing edges.
    /// Expected:
    /// - Path times remain at their pre-set values (no decrease).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CalculateLongestPathTime_PreExistingTimesGreater_PreservesExistingViaMathMax()
    {
        // Arrange
        var node = new DependencyFlowNode("repo", "branch", Guid.NewGuid().ToString())
        {
            OfficialBuildTime = 1.0,
            WorstCasePathTime = 500.0,
            BestCasePathTime = 400.0
        };

        AddEdge(node, worstCasePathTime: 10, bestCasePathTime: 5, prBuildTime: 1, isToolingOnlyEdge: false);

        // Act
        node.CalculateLongestPathTime();

        // Assert
        node.WorstCasePathTime.Should().Be(500.0);
        node.BestCasePathTime.Should().Be(400.0);
    }

    /// <summary>
    /// Verifies that if all outgoing edges are tooling-only, they are filtered out and the result
    /// is based solely on the node's OfficialBuildTime (subject to Math.Max with existing values).
    /// Inputs:
    /// - Only tooling-only outgoing edges.
    /// - No pre-existing larger path times.
    /// Expected:
    /// - WorstCasePathTime == OfficialBuildTime.
    /// - BestCasePathTime == OfficialBuildTime.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CalculateLongestPathTime_AllToolingEdges_ResultEqualsOfficialBuildTime()
    {
        // Arrange
        var node = new DependencyFlowNode("repo", "branch", Guid.NewGuid().ToString())
        {
            OfficialBuildTime = 75.0
        };

        AddEdge(node, worstCasePathTime: 200, bestCasePathTime: 150, prBuildTime: 10, isToolingOnlyEdge: true);

        // Act
        node.CalculateLongestPathTime();

        // Assert
        node.WorstCasePathTime.Should().Be(75.0);
        node.BestCasePathTime.Should().Be(75.0);
    }

    /// <summary>
    /// Verifies that back edges are excluded from edges of interest when non-back edges exist,
    /// ensuring they do not impact the computed times.
    /// Inputs:
    /// - One non-back, non-tooling edge with small times.
    /// - One back, non-tooling edge with very large times.
    /// Expected:
    /// - Computation uses only the non-back edge; the back edge is ignored.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void CalculateLongestPathTime_BackEdgesExcluded_WhenNonBackEdgesExist()
    {
        // Arrange
        var node = new DependencyFlowNode("repo", "branch", Guid.NewGuid().ToString())
        {
            OfficialBuildTime = 10.0
        };

        var nonBack = AddEdge(node, worstCasePathTime: 1, bestCasePathTime: 1, prBuildTime: 2, isToolingOnlyEdge: false, backEdge: false);
        AddEdge(node, worstCasePathTime: 1000, bestCasePathTime: 999, prBuildTime: 100, isToolingOnlyEdge: false, backEdge: true);

        // Act
        node.CalculateLongestPathTime();

        // Assert
        node.WorstCasePathTime.Should().Be(10.0 + nonBack.To.WorstCasePathTime + nonBack.To.PrBuildTime);
        node.BestCasePathTime.Should().Be(10.0 + nonBack.To.BestCasePathTime);
    }

    private static DependencyFlowEdge AddEdge(
        DependencyFlowNode fromNode,
        double worstCasePathTime,
        double bestCasePathTime,
        double prBuildTime,
        bool isToolingOnlyEdge,
        bool backEdge = false)
    {
        // Create the target node with desired times and PR build time.
        var toNode = new DependencyFlowNode("toRepo", "toBranch", Guid.NewGuid().ToString())
        {
            WorstCasePathTime = worstCasePathTime,
            BestCasePathTime = bestCasePathTime,
            PrBuildTime = prBuildTime
        };

        // Create the edge and add to fromNode.
        var edge = new DependencyFlowEdge(fromNode, toNode, null)
        {
            IsToolingOnly = isToolingOnlyEdge,
            BackEdge = backEdge
        };

        fromNode.OutgoingEdges.Add(edge);
        return edge;
    }
}

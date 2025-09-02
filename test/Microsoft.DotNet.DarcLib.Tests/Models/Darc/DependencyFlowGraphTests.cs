// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Models.Darc.UnitTests;

public class DependencyFlowGraphTests
{
    /// <summary>
    /// Verifies that the constructor assigns the provided Nodes and Edges lists directly to the corresponding properties.
    /// Inputs:
    ///  - A List of DependencyFlowNode containing 0, 1, or 2 items (with the 2-item case using duplicate node identities).
    ///  - An empty List of DependencyFlowEdge.
    /// Expected:
    ///  - graph.Nodes references the exact same instance as the provided nodes list.
    ///  - graph.Edges references the exact same instance as the provided edges list.
    /// Notes:
    ///  - Null inputs are not tested because parameters are non-nullable in the source and nullability annotations are disabled.
    /// </summary>
    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_AssignsProvidedLists_PropertiesReferenceTheSameInstances(int nodeCount)
    {
        // Arrange
        var nodes = new List<DependencyFlowNode>();
        if (nodeCount >= 1)
        {
            nodes.Add(new DependencyFlowNode("https://repo/a", "main", "node-a"));
        }
        if (nodeCount >= 2)
        {
            // Duplicate identity values to ensure duplicates are accepted without validation in constructor.
            nodes.Add(new DependencyFlowNode("https://repo/a", "main", "node-a"));
        }

        var edges = new List<DependencyFlowEdge>();

        // Act
        var graph = new DependencyFlowGraph(nodes, edges);

        // Assert
        graph.Nodes.Should().BeSameAs(nodes);
        graph.Edges.Should().BeSameAs(edges);
    }

    /// <summary>
    /// Ensures that the constructor does not clone collections by verifying mutations to the original lists
    /// are reflected in the graph's properties.
    /// Inputs:
    ///  - nodes list initially containing a single node; edges list is empty.
    ///  - After construction, a second node is added to the original nodes list.
    /// Expected:
    ///  - graph.Nodes reference remains the same instance as the original list.
    ///  - graph.Nodes reflects the new item (Count incremented and element reference equality holds).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_ListMutationAfterConstruction_PropertiesReflectMutation()
    {
        // Arrange
        var initialNode = new DependencyFlowNode("https://repo/a", "main", "node-a");
        var nodes = new List<DependencyFlowNode> { initialNode };
        var edges = new List<DependencyFlowEdge>();

        var graph = new DependencyFlowGraph(nodes, edges);

        var addedNode = new DependencyFlowNode("https://repo/b", "release", "node-b");

        // Act
        nodes.Add(addedNode);

        // Assert
        graph.Nodes.Should().BeSameAs(nodes);
        graph.Nodes.Count.Should().Be(2);
        graph.Nodes[1].Should().BeSameAs(addedNode);
    }

    /// <summary>
    /// Ensures that attempting to remove a node that is not present in the graph does not modify the graph.
    /// Inputs:
    ///  - Graph with a single node 'a' and no edges.
    ///  - Attempt to remove an unrelated node 'b' not contained in the graph's Nodes.
    /// Expected:
    ///  - Graph.Nodes remains unchanged and still contains only 'a'.
    ///  - Node 'a' retains empty IncomingEdges and OutgoingEdges.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void RemoveNode_NodeNotInGraph_DoesNothing()
    {
        // Arrange
        var a = new DependencyFlowNode("repoA", "main", "a");
        var b = new DependencyFlowNode("repoB", "main", "b");

        var graph = new DependencyFlowGraph(
            new List<DependencyFlowNode> { a },
            new List<DependencyFlowEdge>());

        // Act
        graph.RemoveNode(b);

        // Assert
        graph.Nodes.Should().HaveCount(1);
        graph.Nodes.Single().Should().Be(a);

        a.IncomingEdges.Should().BeEmpty();
        a.OutgoingEdges.Should().BeEmpty();
    }

    /// <summary>
    /// Validates that removing a node:
    /// - Clears its presence from the graph's Nodes.
    /// - Removes all incoming edges from their source nodes' OutgoingEdges.
    /// - Removes all outgoing edges from targets' IncomingEdges.
    /// - Recalculates targets' InputChannels to an empty set when no incoming edges remain.
    /// Inputs:
    ///  - Node 'victim' with two incoming edges from 'src1' and 'src2'.
    ///  - Node 'victim' with two outgoing edges to 'tgt1' and 'tgt2'.
    ///  - Targets have pre-populated InputChannels to verify recalculation.
    /// Expected:
    ///  - 'victim' removed from graph.Nodes.
    ///  - 'src1' and 'src2' OutgoingEdges are empty.
    ///  - 'tgt1' and 'tgt2' IncomingEdges are empty and InputChannels cleared.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void RemoveNode_NodeWithIncomingAndOutgoingEdges_RemovesReferencesAndRecalculatesTargets()
    {
        // Arrange
        var src1 = new DependencyFlowNode("repo-src1", "main", "src1");
        var src2 = new DependencyFlowNode("repo-src2", "main", "src2");
        var victim = new DependencyFlowNode("repo-victim", "release", "victim");
        var tgt1 = new DependencyFlowNode("repo-tgt1", "branch", "tgt1");
        var tgt2 = new DependencyFlowNode("repo-tgt2", "branch", "tgt2");

        // Incoming edges to victim (from src1, src2)
        var eIn1 = new DependencyFlowEdge(src1, victim, subscription: null);
        var eIn2 = new DependencyFlowEdge(src2, victim, subscription: null);
        src1.OutgoingEdges.Add(eIn1);
        src2.OutgoingEdges.Add(eIn2);
        victim.IncomingEdges.AddRange(new[] { eIn1, eIn2 });

        // Outgoing edges from victim (to tgt1, tgt2)
        var eOut1 = new DependencyFlowEdge(victim, tgt1, subscription: null);
        var eOut2 = new DependencyFlowEdge(victim, tgt2, subscription: null);
        victim.OutgoingEdges.AddRange(new[] { eOut1, eOut2 });
        tgt1.IncomingEdges.Add(eOut1);
        tgt2.IncomingEdges.Add(eOut2);

        // Pre-populate targets' InputChannels so we can verify they are recalculated/cleared
        tgt1.InputChannels.Add("seed-channel-1");
        tgt2.InputChannels.Add("seed-channel-2");

        var graph = new DependencyFlowGraph(
            new List<DependencyFlowNode> { src1, src2, victim, tgt1, tgt2 },
            new List<DependencyFlowEdge> { eIn1, eIn2, eOut1, eOut2 });

        // Act
        graph.RemoveNode(victim);

        // Assert
        graph.Nodes.Should().HaveCount(4);
        graph.Nodes.Should().NotContain(victim);

        src1.OutgoingEdges.Should().BeEmpty();
        src2.OutgoingEdges.Should().BeEmpty();

        tgt1.IncomingEdges.Should().BeEmpty();
        tgt2.IncomingEdges.Should().BeEmpty();

        // RecalculateInputChannels should set InputChannels to the set derived from remaining incoming edges.
        // Since we removed the only incoming edge for each target, the channels should be cleared.
        tgt1.InputChannels.Should().BeEmpty();
        tgt2.InputChannels.Should().BeEmpty();
    }

    /// <summary>
    /// Validates that when an edge exists in the graph's Edges list, RemoveEdge:
    ///  - Removes it from the graph-level edge list.
    ///  - Removes it from the originating node's OutgoingEdges.
    ///  - Removes it from the target node's IncomingEdges.
    ///  - Recalculates the target node's InputChannels to reflect no incoming edges (becomes empty).
    /// Inputs:
    ///  - A graph with two nodes (A -> B) and a single edge present in Edges/OutgoingEdges/IncomingEdges.
    ///  - B has pre-populated InputChannels to ensure recalculation occurs.
    /// Expected:
    ///  - The edge is removed from all collections.
    ///  - B.InputChannels becomes empty.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RemoveEdge_EdgePresent_RemovesFromGraphAndAdjacencyAndClearsTargetInputChannels()
    {
        // Arrange
        var nodeA = new DependencyFlowNode("repoA", "main", "A");
        var nodeB = new DependencyFlowNode("repoB", "main", "B");

        var sub = new Subscription { Channel = new Channel { Name = "channel-a" } };
        var edge = new DependencyFlowEdge(nodeA, nodeB, sub);

        var graph = new DependencyFlowGraph(
            new List<DependencyFlowNode> { nodeA, nodeB },
            new List<DependencyFlowEdge> { edge });

        nodeA.OutgoingEdges.Add(edge);
        nodeB.IncomingEdges.Add(edge);

        // Pre-populate InputChannels to ensure recalculation replaces/clears it
        nodeB.InputChannels.Add("stale-channel");

        // Act
        graph.RemoveEdge(edge);

        // Assert
        graph.Edges.Should().NotContain(edge);
        nodeA.OutgoingEdges.Should().NotContain(edge);
        nodeB.IncomingEdges.Should().NotContain(edge);
        nodeB.InputChannels.Should().BeEmpty();
    }

    /// <summary>
    /// Ensures that when attempting to remove an edge not present in the graph's Edges list,
    /// no collections are modified and target node InputChannels are not recalculated.
    /// Inputs:
    ///  - A graph with one existing edge (A -> B).
    ///  - An unrelated edge (A -> B) instance not in the graph's Edges list is passed to RemoveEdge.
    ///  - B has a known set of InputChannels prior to the call.
    /// Expected:
    ///  - The existing edge remains in all collections.
    ///  - B.InputChannels remains unchanged.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RemoveEdge_EdgeNotInGraph_NoMutationOccurs()
    {
        // Arrange
        var nodeA = new DependencyFlowNode("repoA", "main", "A");
        var nodeB = new DependencyFlowNode("repoB", "main", "B");

        var existingSub = new Subscription { Channel = new Channel { Name = "existing" } };
        var existingEdge = new DependencyFlowEdge(nodeA, nodeB, existingSub);

        var graph = new DependencyFlowGraph(
            new List<DependencyFlowNode> { nodeA, nodeB },
            new List<DependencyFlowEdge> { existingEdge });

        nodeA.OutgoingEdges.Add(existingEdge);
        nodeB.IncomingEdges.Add(existingEdge);

        // Set an initial, known value to detect unintended recalculation
        nodeB.InputChannels.Clear();
        nodeB.InputChannels.Add("pre-existing-input");

        // Create a different edge instance (not in graph)
        var toRemoveSub = new Subscription { Channel = new Channel { Name = "to-remove" } };
        var edgeNotInGraph = new DependencyFlowEdge(nodeA, nodeB, toRemoveSub);

        // Act
        graph.RemoveEdge(edgeNotInGraph);

        // Assert
        graph.Edges.Should().Contain(existingEdge);
        nodeA.OutgoingEdges.Should().Contain(existingEdge);
        nodeB.IncomingEdges.Should().Contain(existingEdge);
        nodeB.InputChannels.Should().BeEquivalentTo(new[] { "pre-existing-input" });
    }

    /// <summary>
    /// Verifies that when removing one of multiple incoming edges to a node,
    /// the target node's InputChannels are recalculated to contain only the remaining edges' channels.
    /// Inputs:
    ///  - A graph with two incoming edges to B: A -> B (channel-a) and C -> B (channel-c).
    ///  - Remove the A -> B edge.
    /// Expected:
    ///  - Only the C -> B edge remains in all relevant collections.
    ///  - B.InputChannels contains exactly "channel-c".
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RemoveEdge_RemainingIncomingEdges_InputChannelsRecomputedToRemainingChannels()
    {
        // Arrange
        var nodeA = new DependencyFlowNode("repoA", "main", "A");
        var nodeB = new DependencyFlowNode("repoB", "main", "B");
        var nodeC = new DependencyFlowNode("repoC", "main", "C");

        var subA = new Subscription { Channel = new Channel { Name = "channel-a" } };
        var subC = new Subscription { Channel = new Channel { Name = "channel-c" } };

        var edgeAtoB = new DependencyFlowEdge(nodeA, nodeB, subA);
        var edgeCtoB = new DependencyFlowEdge(nodeC, nodeB, subC);

        var graph = new DependencyFlowGraph(
            new List<DependencyFlowNode> { nodeA, nodeB, nodeC },
            new List<DependencyFlowEdge> { edgeAtoB, edgeCtoB });

        nodeA.OutgoingEdges.Add(edgeAtoB);
        nodeC.OutgoingEdges.Add(edgeCtoB);
        nodeB.IncomingEdges.Add(edgeAtoB);
        nodeB.IncomingEdges.Add(edgeCtoB);

        // Put a value to be replaced by recalculation
        nodeB.InputChannels.Clear();
        nodeB.InputChannels.Add("stale");

        // Act
        graph.RemoveEdge(edgeAtoB);

        // Assert
        graph.Edges.Should().NotContain(edgeAtoB);
        graph.Edges.Should().Contain(edgeCtoB);

        nodeA.OutgoingEdges.Should().NotContain(edgeAtoB);
        nodeC.OutgoingEdges.Should().Contain(edgeCtoB);

        nodeB.IncomingEdges.Should().NotContain(edgeAtoB);
        nodeB.IncomingEdges.Should().Contain(edgeCtoB);

        nodeB.InputChannels.Should().BeEquivalentTo(new[] { "channel-c" });
    }

    /// <summary>
    /// Verifies that a simple linear chain has no back edges and that the temporary start node/edge are fully removed.
    /// Inputs:
    ///   - Graph: A -> B -> C (C is a sink).
    /// Expected:
    ///   - No edges are marked as back edges.
    ///   - Node and edge counts remain unchanged.
    ///   - C remains a sink (no outgoing edges) after MarkBackEdges.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void MarkBackEdges_LinearChain_NoBackEdgesAndGraphCleaned()
    {
        // Arrange
        var a = new DependencyFlowNode("repoA", "main", "A");
        var b = new DependencyFlowNode("repoB", "main", "B");
        var c = new DependencyFlowNode("repoC", "main", "C");

        var ab = new DependencyFlowEdge(a, b, null);
        var bc = new DependencyFlowEdge(b, c, null);

        a.OutgoingEdges.Add(ab);
        b.IncomingEdges.Add(ab);

        b.OutgoingEdges.Add(bc);
        c.IncomingEdges.Add(bc);

        var nodes = new List<DependencyFlowNode> { a, b, c };
        var edges = new List<DependencyFlowEdge> { ab, bc };

        var graph = new DependencyFlowGraph(nodes, edges);

        var initialNodeCount = graph.Nodes.Count;
        var initialEdgeCount = graph.Edges.Count;

        // Act
        graph.MarkBackEdges();

        // Assert
        ab.BackEdge.Should().Be(false);
        bc.BackEdge.Should().Be(false);

        graph.Nodes.Count.Should().Be(initialNodeCount);
        graph.Edges.Count.Should().Be(initialEdgeCount);
        c.OutgoingEdges.Count.Should().Be(0);
        graph.Nodes.Any(n => n.Id == "start" && n.Repository == "start" && n.Branch == "start").Should().Be(false);
    }

    /// <summary>
    /// Ensures that when the graph has only a cycle and no sink nodes, all edges are marked as back edges
    /// due to the dominator initialization (no propagation without sinks).
    /// Inputs:
    ///   - Graph: A <-> B (A -> B, B -> A), no sinks.
    /// Expected:
    ///   - Both edges are marked as back edges.
    ///   - Node and edge counts remain unchanged (no lingering temporary start node).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void MarkBackEdges_CycleOnly_AllEdgesMarkedAsBackEdges()
    {
        // Arrange
        var a = new DependencyFlowNode("repoA", "main", "A");
        var b = new DependencyFlowNode("repoB", "main", "B");

        var ab = new DependencyFlowEdge(a, b, null);
        var ba = new DependencyFlowEdge(b, a, null);

        a.OutgoingEdges.Add(ab);
        b.IncomingEdges.Add(ab);

        b.OutgoingEdges.Add(ba);
        a.IncomingEdges.Add(ba);

        var nodes = new List<DependencyFlowNode> { a, b };
        var edges = new List<DependencyFlowEdge> { ab, ba };

        var graph = new DependencyFlowGraph(nodes, edges);

        var initialNodeCount = graph.Nodes.Count;
        var initialEdgeCount = graph.Edges.Count;

        // Act
        graph.MarkBackEdges();

        // Assert
        ab.BackEdge.Should().Be(true);
        ba.BackEdge.Should().Be(true);

        graph.Nodes.Count.Should().Be(initialNodeCount);
        graph.Edges.Count.Should().Be(initialEdgeCount);
        graph.Nodes.Any(n => n.Id == "start" && n.Repository == "start" && n.Branch == "start").Should().Be(false);
    }

    /// <summary>
    /// Validates that in a graph with a cycle leading to a sink, only the edge that closes the cycle into a dominator is marked as back edge.
    /// Inputs:
    ///   - Graph: A -> B, B -> A (cycle), and B -> C (sink C).
    /// Expected:
    ///   - B -> A is marked as a back edge (closing the cycle).
    ///   - A -> B and B -> C are not back edges.
    ///   - Node and edge counts remain unchanged and no temporary nodes linger.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void MarkBackEdges_CycleWithSink_OnlyEdgeClosingCycleMarked()
    {
        // Arrange
        var a = new DependencyFlowNode("repoA", "main", "A");
        var b = new DependencyFlowNode("repoB", "main", "B");
        var c = new DependencyFlowNode("repoC", "main", "C"); // sink

        var ab = new DependencyFlowEdge(a, b, null);
        var ba = new DependencyFlowEdge(b, a, null);
        var bc = new DependencyFlowEdge(b, c, null);

        a.OutgoingEdges.Add(ab);
        b.IncomingEdges.Add(ab);

        b.OutgoingEdges.Add(ba);
        a.IncomingEdges.Add(ba);

        b.OutgoingEdges.Add(bc);
        c.IncomingEdges.Add(bc);

        var nodes = new List<DependencyFlowNode> { a, b, c };
        var edges = new List<DependencyFlowEdge> { ab, ba, bc };

        var graph = new DependencyFlowGraph(nodes, edges);

        var initialNodeCount = graph.Nodes.Count;
        var initialEdgeCount = graph.Edges.Count;

        // Act
        graph.MarkBackEdges();

        // Assert
        ba.BackEdge.Should().Be(true);
        ab.BackEdge.Should().Be(false);
        bc.BackEdge.Should().Be(false);

        graph.Nodes.Count.Should().Be(initialNodeCount);
        graph.Edges.Count.Should().Be(initialEdgeCount);
        graph.Nodes.Any(n => n.Id == "start" && n.Repository == "start" && n.Branch == "start").Should().Be(false);
    }

    /// <summary>
    /// Ensures the method does nothing and throws no exceptions when the graph has no nodes.
    /// Inputs:
    ///  - Empty Nodes and Edges collections.
    /// Expected:
    ///  - No exceptions; collections remain empty.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void CalculateLongestBuildPaths_EmptyGraph_NoException()
    {
        // Arrange
        var graph = new DependencyFlowGraph(new List<DependencyFlowNode>(), new List<DependencyFlowEdge>());

        // Act
        graph.CalculateLongestBuildPaths();

        // Assert
        graph.Nodes.Count.Should().Be(0);
        graph.Edges.Count.Should().Be(0);
    }

    /// <summary>
    /// Validates a single root node (no outgoing edges) uses its OfficialBuildTime as both Best and Worst case path times.
    /// Inputs:
    ///  - One node with no outgoing edges. OfficialBuildTime = 10.5.
    /// Expected:
    ///  - BestCasePathTime == 10.5
    ///  - WorstCasePathTime == 10.5
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void CalculateLongestBuildPaths_SingleRoot_UsesOfficialBuildTime()
    {
        // Arrange
        var root = new DependencyFlowNode("repo", "branch", "root")
        {
            OfficialBuildTime = 10.5
        };
        var graph = new DependencyFlowGraph(new List<DependencyFlowNode> { root }, new List<DependencyFlowEdge>());

        // Act
        graph.CalculateLongestBuildPaths();

        // Assert
        root.BestCasePathTime.Should().Be(10.5);
        root.WorstCasePathTime.Should().Be(10.5);
    }

    /// <summary>
    /// Ensures a linear chain A -> B -> C (C is root) aggregates build times correctly in BFS order from roots.
    /// Inputs:
    ///  - C (root): Official = 3, PR = 1
    ///  - B: Official = 4, PR = 2
    ///  - A: Official = 5, PR = 10
    /// Expected:
    ///  - C: Best=3, Worst=3
    ///  - B: Best=3+4=7, Worst=(3+1)+4=8
    ///  - A: Best=7+5=12, Worst=(8+2)+5=15
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void CalculateLongestBuildPaths_LinearChain_ComputesAggregateTimes()
    {
        // Arrange
        var a = new DependencyFlowNode("repoA", "main", "A") { OfficialBuildTime = 5, PrBuildTime = 10 };
        var b = new DependencyFlowNode("repoB", "main", "B") { OfficialBuildTime = 4, PrBuildTime = 2 };
        var c = new DependencyFlowNode("repoC", "main", "C") { OfficialBuildTime = 3, PrBuildTime = 1 };

        var graph = new DependencyFlowGraph(new List<DependencyFlowNode> { a, b, c }, new List<DependencyFlowEdge>());
        Connect(graph, a, b);
        Connect(graph, b, c);

        // Act
        graph.CalculateLongestBuildPaths();

        // Assert
        c.BestCasePathTime.Should().Be(3);
        c.WorstCasePathTime.Should().Be(3);

        b.BestCasePathTime.Should().Be(7);
        b.WorstCasePathTime.Should().Be(8);

        a.BestCasePathTime.Should().Be(12);
        a.WorstCasePathTime.Should().Be(15);
    }

    /// <summary>
    /// Verifies that tooling-only edges are excluded from longest path calculation, and when all
    /// outgoing edges are tooling-only, the node uses only its OfficialBuildTime.
    /// Inputs:
    ///  - A with two outgoing edges to B and C (both roots).
    ///  - Case 1 (allToolingOnly=false): A->B normal, A->C tooling-only; B: Official=10, PR=1; C: Official=20, PR=2; A: Official=5
    ///  - Case 2 (allToolingOnly=true): both edges tooling-only; same times.
    /// Expected:
    ///  - Case 1: A.Best=10+5=15, A.Worst=(10+1)+5=16
    ///  - Case 2: A.Best=5, A.Worst=5
    /// </summary>
    [TestCase(false, TestName = "CalculateLongestBuildPaths_Branching_IgnoresToolingOnlyEdgesAndUsesNonTooling")]
    [TestCase(true, TestName = "CalculateLongestBuildPaths_Branching_AllToolingOnly_UsesOfficialBuildTimeOnly")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void CalculateLongestBuildPaths_Branching_ToolingOnlyBehaviour(bool allToolingOnly)
    {
        // Arrange
        var a = new DependencyFlowNode("repoA", "main", "A") { OfficialBuildTime = 5 };
        var b = new DependencyFlowNode("repoB", "main", "B") { OfficialBuildTime = 10, PrBuildTime = 1 };
        var c = new DependencyFlowNode("repoC", "main", "C") { OfficialBuildTime = 20, PrBuildTime = 2 };

        var graph = new DependencyFlowGraph(new List<DependencyFlowNode> { a, b, c }, new List<DependencyFlowEdge>());
        Connect(graph, a, b, isToolingOnly: allToolingOnly ? true : false);
        Connect(graph, a, c, isToolingOnly: true);

        // Act
        graph.CalculateLongestBuildPaths();

        // Assert
        if (allToolingOnly)
        {
            a.BestCasePathTime.Should().Be(5);
            a.WorstCasePathTime.Should().Be(5);
        }
        else
        {
            a.BestCasePathTime.Should().Be(15);
            a.WorstCasePathTime.Should().Be(16);
        }

        b.BestCasePathTime.Should().Be(10);
        b.WorstCasePathTime.Should().Be(10);

        c.BestCasePathTime.Should().Be(20);
        c.WorstCasePathTime.Should().Be(20);
    }

    /// <summary>
    /// Confirms that when the graph has no roots (every node has at least one outgoing edge, e.g., a cycle),
    /// the traversal does not run and times remain unchanged.
    /// Inputs:
    ///  - Two nodes A and B with edges A->B and B->A (cycle). No roots.
    /// Expected:
    ///  - A and B BestCasePathTime and WorstCasePathTime remain 0.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void CalculateLongestBuildPaths_NoRoots_CyclicGraph_TimesRemainUnchanged()
    {
        // Arrange
        var a = new DependencyFlowNode("repoA", "main", "A") { OfficialBuildTime = 2, PrBuildTime = 3 };
        var b = new DependencyFlowNode("repoB", "main", "B") { OfficialBuildTime = 7, PrBuildTime = 1 };
        var graph = new DependencyFlowGraph(new List<DependencyFlowNode> { a, b }, new List<DependencyFlowEdge>());

        Connect(graph, a, b);
        Connect(graph, b, a);

        // Act
        graph.CalculateLongestBuildPaths();

        // Assert
        a.BestCasePathTime.Should().Be(0);
        a.WorstCasePathTime.Should().Be(0);
        b.BestCasePathTime.Should().Be(0);
        b.WorstCasePathTime.Should().Be(0);
    }

    /// <summary>
    /// Ensures that when all outgoing edges of a node are marked as back edges, the implementation
    /// falls back to considering the full outgoing set (per code), thus still computing times.
    /// Inputs:
    ///  - A->B edge marked BackEdge=true; B is root with Official=7, PR=3; A Official=2.
    /// Expected:
    ///  - B: Best=7, Worst=7
    ///  - A: Best=7+2=9, Worst=(7+3)+2=12
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void CalculateLongestBuildPaths_AllBackEdges_FallbackToFullOutgoingList()
    {
        // Arrange
        var a = new DependencyFlowNode("repoA", "main", "A") { OfficialBuildTime = 2 };
        var b = new DependencyFlowNode("repoB", "main", "B") { OfficialBuildTime = 7, PrBuildTime = 3 };

        var graph = new DependencyFlowGraph(new List<DependencyFlowNode> { a, b }, new List<DependencyFlowEdge>());
        Connect(graph, a, b, backEdge: true);

        // Act
        graph.CalculateLongestBuildPaths();

        // Assert
        b.BestCasePathTime.Should().Be(7);
        b.WorstCasePathTime.Should().Be(7);

        a.BestCasePathTime.Should().Be(9);
        a.WorstCasePathTime.Should().Be(12);
    }

    // Helper: Connect two nodes with an edge and register it in the graph and node adjacency lists.
    private static DependencyFlowEdge Connect(DependencyFlowGraph graph, DependencyFlowNode from, DependencyFlowNode to, bool isToolingOnly = false, bool backEdge = false)
    {
        var e = new DependencyFlowEdge(from, to, null)
        {
            IsToolingOnly = isToolingOnly,
            BackEdge = backEdge
        };
        to.IncomingEdges.Add(e);
        from.OutgoingEdges.Add(e);
        graph.Edges.Add(e);
        return e;
    }

    /// <summary>
    /// Ensures that when the graph has no valid non-tooling start node (empty graph, leaf-only node, or only tooling edges),
    /// MarkLongestBuildPath performs no marking.
    /// Inputs:
    ///  - Scenario "empty": no nodes.
    ///  - Scenario "leaf": single node with no outgoing edges.
    ///  - Scenario "tooling-edges": node with only tooling-only outgoing edges.
    /// Expected:
    ///  - No node or edge has OnLongestBuildPath marked true.
    /// </summary>
    [TestCase("empty")]
    [TestCase("leaf")]
    [TestCase("tooling-edges")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void MarkLongestBuildPath_NoStartNode_NoMarking(string scenario)
    {
        // Arrange
        var nodes = new List<DependencyFlowNode>();
        var edges = new List<DependencyFlowEdge>();

        if (scenario == "leaf")
        {
            var leaf = new DependencyFlowNode("repo1", "main", "leaf");
            // No edges => IsToolingOnly == true, so it is not a valid start node
            nodes.Add(leaf);
        }
        else if (scenario == "tooling-edges")
        {
            var from = new DependencyFlowNode("repo1", "main", "from");
            var to = new DependencyFlowNode("repo2", "main", "to");

            var e = Connect(from, to, isToolingOnly: true, backEdge: false, onPath: false);
            nodes.AddRange(new[] { from, to });
            edges.Add(e);
        }

        var graph = new DependencyFlowGraph(nodes, edges);

        // Act
        graph.MarkLongestBuildPath();

        // Assert
        foreach (var n in graph.Nodes)
        {
            n.OnLongestBuildPath.Should().BeFalse();
        }

        foreach (var e in graph.Edges)
        {
            e.OnLongestBuildPath.Should().BeFalse();
        }
    }

    /// <summary>
    /// Verifies that the method selects the non-tooling node with the highest BestCasePathTime as the start,
    /// then follows edges greedily by the To node's BestCasePathTime, marking both the chosen edge and node.
    /// Inputs:
    ///  - Two candidate start nodes (A and D) with non-tooling edges, where D has higher BestCasePathTime.
    ///  - D has two outgoing non-tooling edges to E (BestCase 100) and F (BestCase 80).
    /// Expected:
    ///  - D.OnLongestBuildPath == true
    ///  - Edge D->E.OnLongestBuildPath == true (chosen as it leads to greater BestCasePathTime)
    ///  - E.OnLongestBuildPath == true
    ///  - A and its edges remain unmarked
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void MarkLongestBuildPath_ChoosesMaxBestCaseNodeAndGreedyEdge_MarksNodesAndEdges()
    {
        // Arrange
        var a = new DependencyFlowNode("repoA", "main", "A") { BestCasePathTime = 10 };
        var b = new DependencyFlowNode("repoA", "main", "B") { BestCasePathTime = 5 };

        var d = new DependencyFlowNode("repoD", "main", "D") { BestCasePathTime = 20 };
        var e = new DependencyFlowNode("repoD", "main", "E") { BestCasePathTime = 100 };
        var f = new DependencyFlowNode("repoD", "main", "F") { BestCasePathTime = 80 };

        var edges = new List<DependencyFlowEdge>
            {
                Connect(a, b, isToolingOnly: false),
                Connect(d, e, isToolingOnly: false),
                Connect(d, f, isToolingOnly: false)
            };

        var graph = new DependencyFlowGraph(new List<DependencyFlowNode> { a, b, d, e, f }, edges);

        // Act
        graph.MarkLongestBuildPath();

        // Assert
        a.OnLongestBuildPath.Should().BeFalse();
        b.OnLongestBuildPath.Should().BeFalse();

        d.OnLongestBuildPath.Should().BeTrue();
        e.OnLongestBuildPath.Should().BeTrue();
        f.OnLongestBuildPath.Should().BeFalse();

        edges.Single(x => x.From == d && x.To == e).OnLongestBuildPath.Should().BeTrue();
        edges.Single(x => x.From == d && x.To == f).OnLongestBuildPath.Should().BeFalse();
        edges.Single(x => x.From == a && x.To == b).OnLongestBuildPath.Should().BeFalse();
    }

    /// <summary>
    /// Ensures that edges marked as BackEdge or ToolingOnly are excluded from the path selection,
    /// resulting in no edge marking when they are the only available edges. The start node is still marked.
    /// Inputs:
    ///  - Start candidate P with BestCasePathTime higher than any other non-tooling node.
    ///  - Outgoing edges: one non-tooling BackEdge, one tooling-only edge.
    /// Expected:
    ///  - P.OnLongestBuildPath == true
    ///  - No edges are marked OnLongestBuildPath
    ///  - Destination nodes remain unmarked
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void MarkLongestBuildPath_ExcludesBackAndToolingEdges_NoEdgeMarked()
    {
        // Arrange
        var p = new DependencyFlowNode("repoP", "main", "P") { BestCasePathTime = 50 };
        var q = new DependencyFlowNode("repoQ", "main", "Q") { BestCasePathTime = 100 };
        var r = new DependencyFlowNode("repoR", "main", "R") { BestCasePathTime = 90 };

        var backEdge = Connect(p, q, isToolingOnly: false, backEdge: true);
        var toolingEdge = Connect(p, r, isToolingOnly: true, backEdge: false);

        var graph = new DependencyFlowGraph(new List<DependencyFlowNode> { p, q, r }, new List<DependencyFlowEdge> { backEdge, toolingEdge });

        // Act
        graph.MarkLongestBuildPath();

        // Assert
        p.OnLongestBuildPath.Should().BeTrue();
        q.OnLongestBuildPath.Should().BeFalse();
        r.OnLongestBuildPath.Should().BeFalse();

        backEdge.OnLongestBuildPath.Should().BeFalse();
        toolingEdge.OnLongestBuildPath.Should().BeFalse();
    }

    /// <summary>
    /// Validates that already-marked edges are ignored, and the next best edge is selected.
    /// Inputs:
    ///  - Start node S with two non-tooling edges to T1 (BestCase 200) and T2 (BestCase 150).
    ///  - Edge S->T1 is pre-marked OnLongestBuildPath.
    /// Expected:
    ///  - S.OnLongestBuildPath == true
    ///  - Edge S->T2 is chosen and marked (since S->T1 is excluded), and T2 is marked
    ///  - Edge S->T1 remains as pre-marked, no additional changes
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void MarkLongestBuildPath_SkipsAlreadyMarkedEdge_SelectsAlternative()
    {
        // Arrange
        var s = new DependencyFlowNode("repoS", "main", "S") { BestCasePathTime = 70 };
        var t1 = new DependencyFlowNode("repoS", "main", "T1") { BestCasePathTime = 200 };
        var t2 = new DependencyFlowNode("repoS", "main", "T2") { BestCasePathTime = 150 };

        var e1 = Connect(s, t1, isToolingOnly: false);
        var e2 = Connect(s, t2, isToolingOnly: false);

        // Pre-mark one edge as on the path to ensure it's excluded from selection
        e1.OnLongestBuildPath = true;

        var graph = new DependencyFlowGraph(new List<DependencyFlowNode> { s, t1, t2 }, new List<DependencyFlowEdge> { e1, e2 });

        // Act
        graph.MarkLongestBuildPath();

        // Assert
        s.OnLongestBuildPath.Should().BeTrue();
        t1.OnLongestBuildPath.Should().BeFalse(); // Not selected by this invocation due to exclusion
        t2.OnLongestBuildPath.Should().BeTrue();  // Selected alternative

        e1.OnLongestBuildPath.Should().BeTrue(); // remains pre-marked
        e2.OnLongestBuildPath.Should().BeTrue(); // selected by MarkLongestPath
    }

    private static DependencyFlowEdge Connect(DependencyFlowNode from, DependencyFlowNode to, bool isToolingOnly, bool backEdge = false, bool onPath = false)
    {
        var edge = new DependencyFlowEdge(from, to, null)
        {
            IsToolingOnly = isToolingOnly,
            BackEdge = backEdge,
            OnLongestBuildPath = onPath
        };
        from.OutgoingEdges.Add(edge);
        to.IncomingEdges.Add(edge);
        return edge;
    }

    /// <summary>
    /// Validates that when both defaultChannels and subscriptions are empty,
    /// no nodes or edges are produced and the BAR client is not called.
    /// Inputs:
    ///  - defaultChannels: []
    ///  - subscriptions: []
    ///  - days: 0
    /// Expected:
    ///  - Resulting graph.Nodes and graph.Edges are empty.
    ///  - IBasicBarClient.GetBuildTimeAsync is never invoked.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BuildAsync_EmptyInputs_ReturnsGraphWithNoNodesOrEdges()
    {
        // Arrange
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var defaultChannels = new List<DefaultChannel>();
        var subscriptions = new List<Subscription>();
        var days = 0;

        // Act
        var graph = await DependencyFlowGraph.BuildAsync(defaultChannels, subscriptions, barClientMock.Object, days);

        // Assert
        graph.Should().NotBeNull();
        graph.Nodes.Should().BeEmpty();
        graph.Edges.Should().BeEmpty();
        barClientMock.Verify(m => m.GetBuildTimeAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    /// <summary>
    /// Ensures that when a DefaultChannel has Id == 0 (default), no BAR call is made,
    /// and the node's OfficialBuildTime and PrBuildTime are set to zero. GoalTimeInMinutes remains default (0).
    /// Inputs:
    ///  - defaultChannels: [ (Id=0, repo, branch, channel.Name="Main") ]
    ///  - subscriptions: []
    ///  - days: 5
    /// Expected:
    ///  - One node exists with OfficialBuildTime=0, PrBuildTime=0, GoalTimeInMinutes=0.
    ///  - OutputChannels contains "Main".
    ///  - IBasicBarClient.GetBuildTimeAsync is never invoked.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BuildAsync_DefaultChannelIdZero_DoesNotCallGetBuildTimeAndSetsZeroTimes()
    {
        // Arrange
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var channel = new Channel(id: 1, name: "Main", classification: "class");
        var dc = new DefaultChannel(id: 0, repository: "https://repo/x", enabled: true)
        {
            Branch = "main",
            Channel = channel
        };
        var defaultChannels = new List<DefaultChannel> { dc };
        var subscriptions = new List<Subscription>();
        var days = 5;

        // Act
        var graph = await DependencyFlowGraph.BuildAsync(defaultChannels, subscriptions, barClientMock.Object, days);

        // Assert
        graph.Nodes.Should().HaveCount(1);
        graph.Edges.Should().BeEmpty();

        var node = graph.Nodes.Single();
        node.Repository.Should().Be("https://repo/x");
        node.Branch.Should().Be("main");
        node.OfficialBuildTime.Should().Be(0);
        node.PrBuildTime.Should().Be(0);
        node.GoalTimeInMinutes.Should().Be(0);
        node.OutputChannels.Should().Contain("Main");

        barClientMock.Verify(m => m.GetBuildTimeAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when DefaultChannel.Id is non-zero, BAR GetBuildTimeAsync is called with the provided 'days' value,
    /// and that node build-time properties are assigned from the returned BuildTime.
    /// Inputs:
    ///  - defaultChannels: [ (Id=123, repo, branch, channel.Name="X") ]
    ///  - subscriptions: []
    ///  - days: parameterized
    /// Expected:
    ///  - One node with OfficialBuildTime=4.5, PrBuildTime=1.25, GoalTimeInMinutes=15.
    ///  - IBasicBarClient.GetBuildTimeAsync(123, days) invoked exactly once.
    /// </summary>
    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(int.MaxValue)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BuildAsync_DefaultChannelNonZeroId_AssignsBuildTimesAndPassesDaysParam(int days)
    {
        // Arrange
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var expectedId = 123;
        var defaultChannel = new DefaultChannel(id: expectedId, repository: "https://repo/a", enabled: true)
        {
            Branch = "main",
            Channel = new Channel(id: 10, name: "X", classification: "class")
        };

        var buildTime = new BuildTime
        {
            OfficialBuildTime = 4.5,
            PrBuildTime = 1.25,
            GoalTimeInMinutes = 15
        };

        barClientMock
            .Setup(m => m.GetBuildTimeAsync(expectedId, days))
            .ReturnsAsync(buildTime);

        var defaultChannels = new List<DefaultChannel> { defaultChannel };
        var subscriptions = new List<Subscription>();

        // Act
        var graph = await DependencyFlowGraph.BuildAsync(defaultChannels, subscriptions, barClientMock.Object, days);

        // Assert
        graph.Nodes.Should().HaveCount(1);
        var node = graph.Nodes.Single();
        node.OfficialBuildTime.Should().Be(4.5);
        node.PrBuildTime.Should().Be(1.25);
        node.GoalTimeInMinutes.Should().Be(15);
        node.OutputChannels.Should().Contain("X");

        barClientMock.Verify(m => m.GetBuildTimeAsync(expectedId, days), Times.Once);
    }

    /// <summary>
    /// Ensures that when BAR returns null build time values, the node uses 0 via null-coalescing.
    /// Inputs:
    ///  - defaultChannels: [ (Id=42, repo, branch, channel.Name="Y") ]
    ///  - subscriptions: []
    ///  - days: 7
    /// Expected:
    ///  - One node with OfficialBuildTime=0, PrBuildTime=0, GoalTimeInMinutes=0.
    ///  - IBasicBarClient.GetBuildTimeAsync(42, 7) invoked once.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BuildAsync_NullBuildTimeValues_DefaultToZero()
    {
        // Arrange
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var id = 42;
        var dc = new DefaultChannel(id: id, repository: "https://repo/y", enabled: true)
        {
            Branch = "develop",
            Channel = new Channel(id: 20, name: "Y", classification: "class")
        };

        barClientMock
            .Setup(m => m.GetBuildTimeAsync(id, 7))
            .ReturnsAsync(new BuildTime()); // All properties null by default

        var defaultChannels = new List<DefaultChannel> { dc };
        var subscriptions = new List<Subscription>();
        var days = 7;

        // Act
        var graph = await DependencyFlowGraph.BuildAsync(defaultChannels, subscriptions, barClientMock.Object, days);

        // Assert
        graph.Nodes.Should().HaveCount(1);
        var node = graph.Nodes.Single();
        node.OfficialBuildTime.Should().Be(0);
        node.PrBuildTime.Should().Be(0);
        node.GoalTimeInMinutes.Should().Be(0);
        node.OutputChannels.Should().Contain("Y");

        barClientMock.Verify(m => m.GetBuildTimeAsync(id, days), Times.Once);
    }

    /// <summary>
    /// Validates that when a subscription has a matching default channel (same channel name, same source repository),
    /// an edge is created from the source node to the destination node and channel mappings are populated.
    /// Inputs:
    ///  - defaultChannels: [ (repo=src, branch=main, channel.Name="Foo") ]
    ///  - subscriptions: [ (sourceRepository=src, targetRepository=dst, targetBranch=main, channel.Name="Foo") ]
    /// Expected:
    ///  - Two nodes (src and dst), one edge from src->dst.
    ///  - destinationNode.InputChannels contains "Foo".
    ///  - sourceNode.OutputChannels contains "Foo".
    ///  - Edge is present in sourceNode.OutgoingEdges and destinationNode.IncomingEdges.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BuildAsync_SubscriptionWithMatchingDefaultChannel_CreatesEdgeAndUpdatesChannels()
    {
        // Arrange
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);

        var srcRepo = "https://repo/src";
        var dstRepo = "https://repo/dst";
        var branch = "main";
        var chanName = "Foo";

        var defaultChannels = new List<DefaultChannel>
            {
                new DefaultChannel(id: 0, repository: srcRepo, enabled: true)
                {
                    Branch = branch,
                    Channel = new Channel(id: 1, name: chanName, classification: "class")
                }
            };

        var subscriptions = new List<Subscription>
            {
                new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: srcRepo, targetRepository: dstRepo, targetBranch: branch, sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
                {
                    Channel = new Channel(id: 2, name: chanName, classification: "class")
                }
            };

        // Act
        var graph = await DependencyFlowGraph.BuildAsync(defaultChannels, subscriptions, barClientMock.Object, days: 1);

        // Assert
        graph.Nodes.Should().HaveCount(2);
        graph.Edges.Should().HaveCount(1);

        var sourceNode = graph.Nodes.First(n => n.Repository == srcRepo && n.Branch == branch);
        var destNode = graph.Nodes.First(n => n.Repository == dstRepo && n.Branch == branch);

        destNode.InputChannels.Should().Contain(chanName);
        sourceNode.OutputChannels.Should().Contain(chanName);

        var edge = graph.Edges.Single();
        ReferenceEquals(edge.From, sourceNode).Should().BeTrue();
        ReferenceEquals(edge.To, destNode).Should().BeTrue();
        sourceNode.OutgoingEdges.Should().Contain(edge);
        destNode.IncomingEdges.Should().Contain(edge);
    }

    /// <summary>
    /// Verifies that channel-name matching for edge creation is case-sensitive: if the names differ by case,
    /// no edge is created, although the destination node's InputChannels is still updated.
    /// Inputs:
    ///  - defaultChannels: [ (repo=src, branch=main, channel.Name="foo") ]
    ///  - subscriptions: [ (sourceRepository=src, targetRepository=dst, targetBranch=main, channel.Name="Foo") ]
    /// Expected:
    ///  - Two nodes (src and dst), zero edges.
    ///  - destinationNode.InputChannels includes "Foo".
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BuildAsync_SubscriptionChannelNameCaseMismatch_DoesNotCreateEdge_ButAddsInputChannel()
    {
        // Arrange
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);

        var srcRepo = "https://repo/src";
        var dstRepo = "https://repo/dst";
        var branch = "main";

        var defaultChannels = new List<DefaultChannel>
            {
                new DefaultChannel(id: 0, repository: srcRepo, enabled: true)
                {
                    Branch = branch,
                    Channel = new Channel(id: 1, name: "foo", classification: "class")
                }
            };

        var subscriptions = new List<Subscription>
            {
                new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: srcRepo, targetRepository: dstRepo, targetBranch: branch, sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
                {
                    Channel = new Channel(id: 2, name: "Foo", classification: "class")
                }
            };

        // Act
        var graph = await DependencyFlowGraph.BuildAsync(defaultChannels, subscriptions, barClientMock.Object, days: 1);

        // Assert
        graph.Nodes.Should().HaveCount(2);
        graph.Edges.Should().BeEmpty();

        var destNode = graph.Nodes.First(n => n.Repository == dstRepo && n.Branch == branch);
        destNode.InputChannels.Should().Contain("Foo");
    }

    /// <summary>
    /// Confirms that GetOrCreateNode uses a case-insensitive key on repo@branch,
    /// so the same node instance is reused for case-variant repository/branch strings.
    /// Inputs:
    ///  - defaultChannels: [ (repo="https://repo/Case", branch="Main", channel.Name="foo") ]
    ///  - subscriptions: [ (sourceRepository="https://other/repo", targetRepository="https://repo/case", targetBranch="main", channel.Name="bar") ]
    /// Expected:
    ///  - Only one node exists (destination and the default channel node are the same).
    ///  - The node has OutputChannels containing "foo" and InputChannels containing "bar".
    ///  - No edges are created (subscription's channel name doesn't match any default channel).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BuildAsync_GetOrCreateNode_KeyIsCaseInsensitive_ReusesNodeForCaseVariantRepoBranch()
    {
        // Arrange
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);

        var repoCased = "https://repo/Case";
        var repoVariant = "https://repo/case";
        var branchCased = "Main";
        var branchVariant = "main";

        var defaultChannels = new List<DefaultChannel>
            {
                new DefaultChannel(id: 0, repository: repoCased, enabled: true)
                {
                    Branch = branchCased,
                    Channel = new Channel(id: 1, name: "foo", classification: "class")
                }
            };

        // Use a channel name that doesn't match default channel's name, ensuring no edge is created.
        var subscriptions = new List<Subscription>
            {
                new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: "https://other/repo", targetRepository: repoVariant, targetBranch: branchVariant, sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
                {
                    Channel = new Channel(id: 2, name: "bar", classification: "class")
                }
            };

        // Act
        var graph = await DependencyFlowGraph.BuildAsync(defaultChannels, subscriptions, barClientMock.Object, days: 3);

        // Assert
        graph.Nodes.Should().HaveCount(1);
        graph.Edges.Should().BeEmpty();

        var node = graph.Nodes.Single();
        node.Repository.Should().Be(repoCased);
        node.Branch.Should().Be(branchCased);
        node.OutputChannels.Should().Contain("foo");
        node.InputChannels.Should().Contain("bar");
    }

    /// <summary>
    /// Verifies that IsInterestingNode returns the expected boolean indicating whether the target channel
    /// exactly matches (case-sensitive) any channel in the node's OutputChannels.
    /// Inputs:
    ///  - A DependencyFlowNode seeded with a set of output channels.
    ///  - A targetChannel string to search for.
    /// Expected:
    ///  - True only when any OutputChannels entry equals targetChannel using case-sensitive equality.
    /// Notes:
    ///  - Demonstrates that differing case results in false due to the method's use of '=='.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(IsInterestingNode_TestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void IsInterestingNode_VariousOutputs_ReturnsExpected(IEnumerable<string> outputChannels, string targetChannel, bool expected)
    {
        // Arrange
        var node = new DependencyFlowNode("https://repo.example/org/project", "main", "node-1");
        foreach (var ch in outputChannels)
        {
            node.OutputChannels.Add(ch);
        }

        // Act
        var result = DependencyFlowGraph.IsInterestingNode(targetChannel, node);

        // Assert
        result.Should().Be(expected);
    }

    private static IEnumerable<TestCaseData> IsInterestingNode_TestCases()
    {
        yield return new TestCaseData(Array.Empty<string>(), "stable", false)
            .SetName("IsInterestingNode_EmptyOutputChannels_ReturnsFalse");

        yield return new TestCaseData(new[] { "stable", "Release" }, "stable", true)
            .SetName("IsInterestingNode_ExactMatchExists_ReturnsTrue");

        yield return new TestCaseData(new[] { "Release" }, "release", false)
            .SetName("IsInterestingNode_CaseDiffers_ReturnsFalse");

        yield return new TestCaseData(new[] { "" }, "", true)
            .SetName("IsInterestingNode_EmptyStringChannelAndTarget_ReturnsTrue");

        yield return new TestCaseData(new[] { "  " }, "  ", true)
            .SetName("IsInterestingNode_WhitespaceChannelAndTarget_ReturnsTrue");

        yield return new TestCaseData(new[] { "preview", "rel/*", "rel-1.0" }, "rel/*", true)
            .SetName("IsInterestingNode_SpecialCharactersExactMatch_ReturnsTrue");

        yield return new TestCaseData(new[] { "release " }, "release", false)
            .SetName("IsInterestingNode_TrailingSpaceInChannel_NoMatch_ReturnsFalse");

        yield return new TestCaseData(new[] { new string('x', 2048) }, new string('x', 2048), true)
            .SetName("IsInterestingNode_VeryLongChannelExactMatch_ReturnsTrue");

        yield return new TestCaseData(new[] { "release" }, "Release", false)
            .SetName("IsInterestingNode_CaseDiffersOpposite_ReturnsFalse");

        yield return new TestCaseData(new[] { "alpha", "beta", "gamma" }, "delta", false)
            .SetName("IsInterestingNode_NoMatchingChannel_ReturnsFalse");
    }

    /// <summary>
    /// Validates IsInterestingEdge behavior across combinations of subscription enabled flag,
    /// includeDisabledSubscriptions flag, and includedFrequencies contents.
    /// Inputs (parameterized):
    ///  - enabled: Subscription.Enabled value.
    ///  - includeDisabledSubscriptions: Whether disabled subscriptions should be considered.
    ///  - updateFrequency: SubscriptionPolicy.UpdateFrequency value (including out-of-range case).
    ///  - includedFrequencies: The set of allowed frequencies as strings (case-insensitive comparison).
    /// Expected:
    ///  - Returns false when disabled and includeDisabledSubscriptions is false.
    ///  - Otherwise returns true only if includedFrequencies contains the string representation of UpdateFrequency (OrdinalIgnoreCase).
    ///  - Trailing/leading whitespace in includedFrequencies prevents a match.
    ///  - For out-of-range enum values, the numeric ToString() must be present in includedFrequencies to match.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(IsInterestingEdge_Cases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void IsInterestingEdge_VariousInputs_ReturnsExpected(
        bool enabled,
        bool includeDisabledSubscriptions,
        UpdateFrequency updateFrequency,
        string[] includedFrequencies,
        bool expected)
    {
        // Arrange
        var from = new DependencyFlowNode("repoA", "main", "A");
        var to = new DependencyFlowNode("repoB", "main", "B");

        var subscription = new Subscription(
            id: Guid.NewGuid(),
            enabled: enabled,
            sourceEnabled: true,
            sourceRepository: "https://example.com/src",
            targetRepository: "https://example.com/tgt",
            targetBranch: "main",
            sourceDirectory: "src",
            targetDirectory: "tgt",
            pullRequestFailureNotificationTags: "",
            excludedAssets: new List<string>())

        {
            Policy = new SubscriptionPolicy(batchable: false, updateFrequency: updateFrequency)
        };

        var edge = new DependencyFlowEdge(from, to, subscription);

        // Act
        var result = DependencyFlowGraph.IsInterestingEdge(edge, includeDisabledSubscriptions, includedFrequencies);

        // Assert
        result.Should().Be(expected);
    }

    private static IEnumerable<TestCaseData> IsInterestingEdge_Cases()
    {
        // Disabled subscription should be ignored when includeDisabledSubscriptions == false even if frequency matches
        yield return new TestCaseData(
            false, /* enabled */
            false, /* includeDisabledSubscriptions */
            UpdateFrequency.EveryDay,
            new[] { "EveryDay" },
            false
        ).SetName("IsInterestingEdge_DisabledAndExcluded_False");

        // Disabled subscription is allowed when includeDisabledSubscriptions == true and frequency matches
        yield return new TestCaseData(
            false,
            true,
            UpdateFrequency.EveryDay,
            new[] { "EveryDay" },
            true
        ).SetName("IsInterestingEdge_DisabledButIncludedAndFrequencyMatches_True");

        // Enabled subscription but frequency not included -> false
        yield return new TestCaseData(
            true,
            false,
            UpdateFrequency.EveryBuild,
            new[] { "EveryDay", "TwiceDaily" },
            false
        ).SetName("IsInterestingEdge_EnabledButFrequencyNotIncluded_False");

        // Case-insensitive match should succeed
        yield return new TestCaseData(
            true,
            false,
            UpdateFrequency.EveryBuild,
            new[] { "everybuild" },
            true
        ).SetName("IsInterestingEdge_CaseInsensitiveFrequencyMatch_True");

        // Empty includedFrequencies should fail
        yield return new TestCaseData(
            true,
            false,
            UpdateFrequency.EveryWeek,
            Array.Empty<string>(),
            false
        ).SetName("IsInterestingEdge_EmptyIncludedFrequencies_False");

        // Trailing whitespace prevents match
        yield return new TestCaseData(
            true,
            false,
            UpdateFrequency.EveryMonth,
            new[] { "EveryMonth " },
            false
        ).SetName("IsInterestingEdge_FrequencyWithTrailingWhitespace_False");

        // Out-of-range enum value must match numeric ToString()
        yield return new TestCaseData(
            true,
            false,
            (UpdateFrequency)999,
            new[] { "999" },
            true
        ).SetName("IsInterestingEdge_OutOfRangeEnumValueNumericStringMatch_True");
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Darc.Tests;

/// <summary>
///     A driver that sets and cleans up a dependency flow graph test.
///     Specifically, this class:
///     - Takes an input flow graph and prunes, marks back edges and
///       determines the longest build path
///     - Enables comparison of expected outputs.
///     - Cleans up after test
/// </summary>
internal class DependencyFlowTestDriver
{
    private const string TestFilesInput = "DependencyFlowGraph";
    private const string InputRootDir = "inputs";
    private const string InputJsonFile = "input.json";
    public static string OutputJsonFile { get => "output.json";}
    private string RootInputsPath { get => Path.Combine(Environment.CurrentDirectory, InputRootDir, TestFilesInput, field); }
         
    public DependencyFlowTestDriver(string testName)
    {
        RootInputsPath = testName;
    }

    public DependencyFlowGraph GetDependencyFlowGraph(string channelName, bool includeBuildTimes, IEnumerable<string> includedFrequencies, bool includeDisabledSubscriptions)
    {
        // Deserialize the input file
        string inputGraphPath = Path.Combine(RootInputsPath, InputJsonFile);

        DependencyFlowGraph flowGraph = JsonConvert.DeserializeObject<DependencyFlowGraph>(File.ReadAllText(inputGraphPath));

        List<DependencyFlowEdge> newEdgeList = [];

        foreach (var edge in flowGraph.Edges)
        {
            DependencyFlowNode to = flowGraph.Nodes.First(n => n.Id == edge.To.Id);
            DependencyFlowNode from = flowGraph.Nodes.First(n => n.Id == edge.From.Id);

            var newEdge = new DependencyFlowEdge(from, to, edge.Subscription);

            to.IncomingEdges.Add(newEdge);
            from.OutgoingEdges.Add(newEdge);

            newEdgeList.Add(newEdge);
        }

        flowGraph.Edges = newEdgeList;
            
        if (channelName != null)
        {
            flowGraph.PruneGraph(
                node => DependencyFlowGraph.IsInterestingNode(channelName, node), 
                edge => DependencyFlowGraph.IsInterestingEdge(edge, includeDisabledSubscriptions, includedFrequencies));
        }

        if (includeBuildTimes)
        {
            flowGraph.MarkBackEdges();
            flowGraph.CalculateLongestBuildPaths();
            flowGraph.MarkLongestBuildPath();
        }

        return flowGraph;
    }

    public static void AssertFlowNodeListIsEqual(IEnumerable<DependencyFlowNode> nodes, IEnumerable<DependencyFlowNode> expectedNodes)
    {
        // Check that we have all of the expected nodes
        foreach (var expectedNode in expectedNodes)
        {
            DependencyFlowNode matchingNode = nodes.FirstOrDefault(n => n.Id == expectedNode.Id);
            matchingNode.Should().NotBeNull();

            AssertFlowNodeIsEqual(matchingNode, expectedNode);
        }

        // Confirm that we don't have any extra nodes
        foreach (var node in nodes)
        {
            DependencyFlowNode matchingNode = expectedNodes.FirstOrDefault(n => n.Id == node.Id);
            matchingNode.Should().NotBeNull();

            AssertFlowNodeIsEqual(matchingNode, node);
        }
    }

    public static void AssertFlowNodeIsEqual(DependencyFlowNode node, DependencyFlowNode expectedNode)
    {
        expectedNode.Repository.Should().Be(node.Repository);
        expectedNode.Branch.Should().Be(node.Branch);
        expectedNode.OfficialBuildTime.Should().Be(node.OfficialBuildTime);
        expectedNode.PrBuildTime.Should().Be(node.PrBuildTime);
        expectedNode.BestCasePathTime.Should().Be(node.BestCasePathTime);
        expectedNode.WorstCasePathTime.Should().Be(node.WorstCasePathTime);
        expectedNode.OnLongestBuildPath.Should().Be(node.OnLongestBuildPath);
    }

    public static void AssertFlowEdgeListIsEqual(List<DependencyFlowEdge> edges, List<DependencyFlowEdge> expectedEdges)
    {
        // Check that we have all the expected edges
        foreach (var expectedEdge in expectedEdges)
        {
            DependencyFlowEdge matchingEdge = edges.FirstOrDefault(e => e.Subscription.Id == expectedEdge.Subscription.Id);
            matchingEdge.Should().NotBeNull();

            AssertFlowEdgeIsEqual(matchingEdge, expectedEdge);
        }

        // Confirm we do not have any extra edges
        foreach (var edge in edges)
        {
            DependencyFlowEdge matchingEdge = edges.FirstOrDefault(e => e.Subscription.Id == edge.Subscription.Id);
            matchingEdge.Should().NotBeNull();

            AssertFlowEdgeIsEqual(matchingEdge, edge);
        }
    }

    public static void AssertFlowEdgeIsEqual(DependencyFlowEdge edge, DependencyFlowEdge expectedEdge)
    {
        AssertFlowNodeIsEqual(edge.To, expectedEdge.To);
        AssertFlowNodeIsEqual(edge.From, expectedEdge.From);
        expectedEdge.OnLongestBuildPath.Should().Be(edge.OnLongestBuildPath);
        expectedEdge.BackEdge.Should().Be(edge.BackEdge);
    }

    public static void GetGraphAndCompare(string testInputsName, 
        Func<DependencyFlowTestDriver, DependencyFlowGraph> testFunc)
    {
        var dependencyFlowTestDriver = new DependencyFlowTestDriver(testInputsName);

        DependencyFlowGraph flowGraph = testFunc(dependencyFlowTestDriver);
            
        DependencyFlowGraph expectedGraph = JsonConvert.DeserializeObject<DependencyFlowGraph>(
            File.ReadAllText(Path.Combine(dependencyFlowTestDriver.RootInputsPath, OutputJsonFile)));

        AssertFlowNodeListIsEqual(flowGraph.Nodes, expectedGraph.Nodes);
        AssertFlowEdgeListIsEqual(flowGraph.Edges, expectedGraph.Edges);
    }
}

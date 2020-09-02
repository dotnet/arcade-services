// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using FluentAssertions;
using Microsoft.DotNet.Maestro.Client.Models;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.DotNet.DarcLib.Tests
{
    public class DependencyFlowNodeTests
    {
        [Test]
        public void VerifyPathTimeIsEqualToOfficialBuildTimeWhenThereAreNoOutgoingEdges()
        {
            var node = new DependencyFlowNode("test", "test", Guid.NewGuid().ToString())
            {
                OfficialBuildTime = 100.0,
                OutgoingEdges = new List<DependencyFlowEdge>()
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

        private DependencyFlowEdge AddEdge(
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

            var subscription = new Subscription(Guid.NewGuid(), true, "source", "target", "test");
            subscription.LastAppliedBuild = new Build(
                id: 1,
                dateProduced: DateTimeOffset.Now,
                staleness: 0,
                released: false,
                stable: true,
                commit: "7a7e5c82abd287262a6efaf29902bb84a7bd81af",
                channels: ImmutableList<Channel>.Empty,
                assets: ImmutableList<Asset>.Empty,
                dependencies: new List<BuildRef>
                {
                    new BuildRef(buildId: 1, isProduct: !isToolingOnlyEdge, timeToInclusionInMinutes: 1)
                }.ToImmutableList(),
                incoherencies: ImmutableList<BuildIncoherence>.Empty);

            var edge = new DependencyFlowEdge(fromNode, toNode, subscription)
            {
                IsToolingOnly = isToolingOnlyEdge
            };

            fromNode.OutgoingEdges.Add(edge);
            return edge;
        }
    }
}

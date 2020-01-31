// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Microsoft.DotNet.DarcLib
{
    public class DependencyFlowNode
    {
        public DependencyFlowNode(string repository, string branch, string id)
        {
            Repository = repository;
            Branch = branch;
            Id = id;
            OutputChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            InputChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            OutgoingEdges = new List<DependencyFlowEdge>();
            IncomingEdges = new List<DependencyFlowEdge>();
            WorstCasePathTime = 0;
            BestCasePathTime = 0;
            OnLongestBuildPath = false;
        }

        public readonly string Repository;
        public readonly string Branch;
        public readonly string Id;

        public HashSet<string> OutputChannels { get; set; }
        public HashSet<string> InputChannels { get; set; }

        [JsonIgnore]
        public List<DependencyFlowEdge> OutgoingEdges { get; set; }
        [JsonIgnore]
        public List<DependencyFlowEdge> IncomingEdges { get; set; }

        public double OfficialBuildTime { get; set; }
        public double PrBuildTime { get; set; }

        public double WorstCasePathTime { get; set; }
        public double BestCasePathTime { get; set; }
        public bool OnLongestBuildPath { get; set; }

        public void CalculateLongestPathTime()
        {
            // If the node does not have any outgoing edges, then it is a root, and its official build time is
            // both its best and worst case time. Otherwise, its worst case path time is the slowest of its
            // outgoing edges' worst case + Pr time for that edge + its official build time. Its best case 
            // is the worst of its outgoing edges' best case + its official built time.
            if (OutgoingEdges.Count == 0)
            {
                WorstCasePathTime = OfficialBuildTime;
                BestCasePathTime = OfficialBuildTime;
            }
            else
            {
                // Our edges of interest are those that are not back edges
                var edgesOfInterest = OutgoingEdges.Where(e => !e.BackEdge).ToList();

                // If all of the edges were marked as backedges, use the full OutgoingEdges list
                if (edgesOfInterest.Count == 0)
                {
                    edgesOfInterest = OutgoingEdges;
                }

                double worstCasePathTime = edgesOfInterest.Max(e => e.To.WorstCasePathTime + e.To.PrBuildTime) + OfficialBuildTime;
                WorstCasePathTime = worstCasePathTime > WorstCasePathTime ? worstCasePathTime : WorstCasePathTime;

                double bestCasePathTime = edgesOfInterest.Max(e => e.To.BestCasePathTime) + OfficialBuildTime;
                BestCasePathTime = bestCasePathTime > BestCasePathTime ? bestCasePathTime : bestCasePathTime;
            }
        }
    }
}

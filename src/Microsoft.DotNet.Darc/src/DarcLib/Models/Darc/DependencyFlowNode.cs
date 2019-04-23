// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib
{
    public class DependencyFlowNode
    {
        public DependencyFlowNode(string repository, string branch)
        {
            Repository = repository;
            Branch = branch;
            OutputChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            InputChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            OutgoingEdges = new List<DependencyFlowEdge>();
            IncomingEdges = new List<DependencyFlowEdge>();
        }

        public readonly string Repository;
        public readonly string Branch;

        public HashSet<string> OutputChannels { get; set; }
        public HashSet<string> InputChannels { get; set; }

        public List<DependencyFlowEdge> OutgoingEdges { get; set; }
        public List<DependencyFlowEdge> IncomingEdges { get; set; }
    }
}

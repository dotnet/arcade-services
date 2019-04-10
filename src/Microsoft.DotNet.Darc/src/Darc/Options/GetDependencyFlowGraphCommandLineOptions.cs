// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib;
using System.Collections.Generic;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("get-flow-graph", HelpText = "Get dependency flow graph")]
    internal class GetDependencyFlowGraphCommandLineOptions : CommandLineOptions
    {
        [Option("graphviz", HelpText = @"Writes the flow graph in GraphViz (dot) form, into the specified file.")]
        public string GraphVizOutputFile { get; set; }

        [Option("include-disabled", HelpText = @"Include edges that do not flow or are disabled.")]
        public bool IncludeDisabledEdges { get; set; }

        [Option("channel", HelpText = @"Only include nodes/edges with flow on this channel.")]
        public string Channel { get; set; }

        public override Operation GetOperation()
        {
            return new GetDependencyFlowGraphOperation(this);
        }
    }
}

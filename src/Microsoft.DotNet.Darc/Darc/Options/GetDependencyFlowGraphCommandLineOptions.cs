// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-flow-graph", HelpText = "Get dependency flow graph")]
internal class GetDependencyFlowGraphCommandLineOptions : CommandLineOptions<GetDependencyFlowGraphOperation>
{
    [Option("graphviz", HelpText = @"Writes the flow graph in GraphViz (dot) form, into the specified file.")]
    [RedactFromLogging]
    public string GraphVizOutputFile { get; set; }

    [Option("include-disabled-subscriptions", HelpText = @"Include edges that have disabled subscriptions")]
    public bool IncludeDisabledSubscriptions { get; set; }

    [Option("frequencies", Separator = ',', Default = new string[] { "everyMonth", "everyTwoWeeks", "everyWeek", "twiceDaily", "everyDay", "everyBuild", "none", },
        HelpText = @"Include only subscriptions with the specific update frequencies in the graph.")]
    public IEnumerable<string> IncludedFrequencies { get; set; }

    [Option("channel", HelpText = @"Only include nodes/edges with flow on this channel.")]
    public string Channel { get; set; }

    [Option("include-build-times", HelpText = @"Include build times for nodes and edges")]
    public bool IncludeBuildTimes { get; set; }

    [Option("days", Default = 7, HelpText = @"Number of Days to summarize build times over")]
    public int Days { get; set; }
}

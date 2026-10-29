// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetDependencyFlowGraphCommandLineOptions : CommandLineOptions<GetDependencyFlowGraphOperation>
{
    public override string Verb => "get-flow-graph";

    public override string VerbDescription => "Get dependency flow graph";

    [RedactFromLogging]
    public string GraphVizOutputFile { get; set; }

    public bool IncludeDisabledSubscriptions { get; set; }

    public IEnumerable<string> IncludedFrequencies { get; set; }

    public string Channel { get; set; }

    public bool IncludeBuildTimes { get; set; }

    public int Days { get; set; }

    protected override void Configure()
    {
        Add<string>("--graphviz", @"Writes the flow graph in GraphViz (dot) form, into the specified file.", value => GraphVizOutputFile = value);
        Add<bool>("--include-disabled-subscriptions", @"Include edges that have disabled subscriptions", value => IncludeDisabledSubscriptions = value);
        AddSeparatedList(
            ',',
            "--frequencies",
            @"Include only subscriptions with the specific update frequencies in the graph.",
            value => IncludedFrequencies = value,
            defaultValue: () => ["everyMonth", "everyTwoWeeks", "everyWeek", "twiceDaily", "everyDay", "everyBuild", "none",]);
        Add<string>("--channel", @"Only include nodes/edges with flow on this channel.", value => Channel = value);
        Add<bool>("--include-build-times", @"Include build times for nodes and edges", value => IncludeBuildTimes = value);
        Add<int>("--days", @"Number of Days to summarize build times over", value => Days = value, defaultValue: () => 7);
        base.Configure();
    }
}

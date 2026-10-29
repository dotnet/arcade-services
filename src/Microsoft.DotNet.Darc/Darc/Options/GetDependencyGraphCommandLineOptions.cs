// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetDependencyGraphCommandLineOptions : CommandLineOptions<GetDependencyGraphOperation>
{
    public override string Verb => "get-dependency-graph";

    public override string VerbDescription => "Get local dependencies.";

    public bool Local { get; set; }

    public string RepoUri { get; set; }

    [RedactFromLogging]
    public string Version { get; set; }

    public string AssetName { get; set; }

    [RedactFromLogging]
    public string ReposFolder { get; set; }

    public IEnumerable<string> RemotesMap { get; set; }

    public bool Flat { get; set; }

    [RedactFromLogging]
    public string GraphVizOutputFile { get; set; }

    [RedactFromLogging]
    public string OutputFile { get; set; }

    public bool IncludeToolset { get; set; }

    public bool SkipBuildLookup { get; set; }

    public string DeltaFrom { get; set; }

    public bool IncludeCoherency { get; set; }

    protected override void Configure()
    {
        Add<bool>("--local", "-l", "Get the graph using only local information.  Requires that repos-folder be passed.", value => Local = value);
        Add<string>("--repo", "If set, gather dependency information from the remote repository. Requires --version.", value => RepoUri = value);
        Add<string>("--version", "-v", "Branch, commit or tag to look up if looking up version information remotely.", value => Version = value);
        Add<string>("--asset-name", "Get the graph based on a single asset and not the whole Version.Details.xml contents.", value => AssetName = value);
        Add<string>("--repos-folder", @"Full path to folder where all the repos are locally stored. i.e. C:\repos", value => ReposFolder = value);
        AddSeparatedList(
            ';',
            "--remotes-map",
            @"';' separated key value pair defining the remote to local path mapping. i.e 'https://github.com/dotnet/arcade,C:\repos\arcade;'"
                + @"https://github.com/dotnet/corefx,C:\repos\corefx.",
            value => RemotesMap = value);
        Add<bool>("--flat", "-f", @"Returns a unique set of repository+sha combination.", value => Flat = value);
        Add<string>("--graphviz", @"Writes the repository graph in GraphViz (dot) form, into the specified file.", value => GraphVizOutputFile = value);
        Add<string>("--output-file", @"Writes the non-GraphViz (dot) output to the specified file into the specified file.", value => OutputFile = value);
        Add<bool>("--include-toolset", "Include toolset dependencies.", value => IncludeToolset = value);
        Add<bool>("--skip-builds", "Do not look up build information.", value => SkipBuildLookup = value);
        Add<string>(
            "--delta-from",
            "Determine the delta of each node in the graph from a target. " +
                "Valid values: [none, newest-in-channel, newest-in-graph]",
            value => DeltaFrom = value,
            defaultValue: () => "newest-in-graph");
        Add<bool>("--coherency", "Report coherency information.", value => IncludeCoherency = value);
        base.Configure();
    }
}

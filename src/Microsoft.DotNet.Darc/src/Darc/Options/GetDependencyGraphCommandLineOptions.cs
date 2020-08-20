// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib;
using System.Collections.Generic;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("get-dependency-graph", HelpText = "Get local dependencies.")]
    internal class GetDependencyGraphCommandLineOptions : CommandLineOptions
    {
        [Option('l', "local", HelpText = "Get the graph using only local information.  Requires that repos-folder be passed.")]
        public bool Local { get; set; }

        [Option("repo", HelpText = "If set, gather dependency information from the remote repository. Requires --version.")]
        public string RepoUri { get; set; }

        [Option('v', "version", HelpText = "Branch, commit or tag to look up if looking up version information remotely.")]
        [RedactFromLogging]
        public string Version { get; set; }

        [Option("asset-name", HelpText = "Get the graph based on a single asset and not the whole Version.Details.xml contents.")]
        public string AssetName { get; set; }

        [Option("repos-folder", HelpText = @"Full path to folder where all the repos are locally stored. i.e. C:\repos")]
        [RedactFromLogging]
        public string ReposFolder { get; set; }

        [Option("remotes-map", Separator = ';', HelpText = @"';' separated key value pair defining the remote to local path mapping. i.e 'https://github.com/dotnet/arcade,C:\repos\arcade;'"
           + @"https://github.com/dotnet/corefx,C:\repos\corefx.")]
        public IEnumerable<string> RemotesMap { get; set; }

        [Option('f', "flat", HelpText = @"Returns a unique set of repository+sha combination.")]
        public bool Flat { get; set; }

        [Option("graphviz", HelpText = @"Writes the repository graph in GraphViz (dot) form, into the specified file.")]
        [RedactFromLogging]
        public string GraphVizOutputFile { get; set; }

        [Option("output-file", HelpText = @"Writes the non-GraphViz (dot) output to the specified file into the specified file.")]
        [RedactFromLogging]
        public string OutputFile { get; set; }

        [Option("include-toolset", HelpText = "Include toolset dependencies.")]
        public bool IncludeToolset { get; set; }

        [Option("skip-builds", HelpText = "Do not look up build information.")]
        public bool SkipBuildLookup { get; set; }

        [Option("delta-from", Default = "newest-in-graph", HelpText = "Determine the delta of each node in the graph from a target. " +
            "Valid values: [none, newest-in-channel, newest-in-graph]")]
        public string DeltaFrom { get; set; }

        [Option("coherency", HelpText = "Report coherency information.")]
        public bool IncludeCoherency { get; set; }

        public override Operation GetOperation()
        {
            return new GetDependencyGraphOperation(this);
        }
    }
}

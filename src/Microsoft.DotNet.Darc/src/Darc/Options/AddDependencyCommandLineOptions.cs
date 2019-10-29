// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("add-dependency", HelpText = "Add a new dependency to version files.")]
    internal class AddDependencyCommandLineOptions : CommandLineOptions
    {
        [Option('n', "name", Required = true, HelpText = "Name of dependency to add.")]
        public string Name { get; set; }

        [Option('t', "type", Required = true, HelpText = "'toolset' or 'product'.")]
        public string Type { get; set; }

        [Option('v', "version", HelpText = "Dependency version.")]
        public string Version { get; set; }

        [Option('r', "repo", Required = true, HelpText = "Repository where the dependency was built.")]
        public string RepoUri { get; set; }

        [Option('c', "commit", HelpText = "SHA at which the dependency was produced.")]
        public string Commit { get; set; }

        [Option("pinned", HelpText = "Whether the dependency is pinned or not.")]
        public bool Pinned { get; set; }

        [Option("coherent-parent", HelpText = "Restrict updates to this dependency based on version of a dependency from another repo. " +
            "See https://github.com/dotnet/arcade/blob/master/Documentation/DependencyDescriptionFormat.md#dependency-description-overview for more information.")]
        public string CoherentParentDependencyName { get; set; }

        public override Operation GetOperation()
        {
            return new AddDependencyOperation(this);
        }
    }
}

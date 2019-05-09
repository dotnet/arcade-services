// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using System.Collections.Generic;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("clone", HelpText = "Clone a remote repo and all of its dependency repos")]

    internal class CloneCommandLineOptions : CommandLineOptions
    {
        [Option("repo", HelpText = "Remote repository to start the clone operation at.  If none specified, clone all that the current repo depends on.")]
        public string RepoUri { get; set; }

        [Option('v', "version", HelpText = "Branch, commit or tag to start at in the remote repository.  Required if repo is specified.")]
        public string Version { get; set; }

        [Option("repos-folder", HelpText = @"Full path to folder where all the repos will be cloned to, e.g. C:\repos.  Default: current directory.")]
        public string ReposFolder { get; set; }

        [Option("git-dir-folder", HelpText = @"Advanced: Full path to folder where .git folders will be stored, e.g. C:\myrepos\.git\modules.  Default: each repo's folder.")]
        public string GitDirFolder { get; set; }

        [Option("include-toolset", HelpText = "Include toolset dependencies.")]
        public bool IncludeToolset { get; set; }

        [Option("ignore-repos", Separator = ';', HelpText = "Semicolon-separated list of repo URIs to ignore.  e.g. 'https://dev.azure.com/devdiv/DevDiv/_git/DotNet-Trusted;https://github.com/dotnet/arcade-services'")]
        public IEnumerable<string> IgnoredRepos { get; set; }

        [Option('d', "depth", Default = uint.MaxValue, HelpText = "Depth to clone the repos to.  Defaults to infinite.")]
        public uint CloneDepth { get; set; }

        public override Operation GetOperation()
        {
            return new CloneOperation(this);
        }
    }
}

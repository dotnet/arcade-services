// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("clone", HelpText = "clone a remote repo and all of its dependency repos")]

    internal class CloneCommandLineOptions : CommandLineOptions
    {
        [Option("repo", HelpText = "Remote repository to start the clone operation at.  If none specified, clone all that the current repo depends on.")]
        public string RepoUri { get; set; }

        [Option('v', "version", HelpText = "Branch, commit or tag to start at in the remote repository.  Required if repo is specified.")]
        public string Version { get; set; }

        [Option("repos-folder", HelpText = @"Full path to folder where all the repos will be cloned to. i.e. C:\repos.  Default: current directory.")]
        public string ReposFolder { get; set; }

        [Option("include-toolset", HelpText = "Include toolset dependencies.")]
        public bool IncludeToolset { get; set; }

        public override Operation GetOperation()
        {
            return new CloneOperation(this);
        }
    }
}

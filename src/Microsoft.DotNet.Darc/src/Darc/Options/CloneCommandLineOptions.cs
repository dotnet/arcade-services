using System;
using System.Collections.Generic;
using System.Text;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("clone", HelpText = "clone a remote repo and all of its dependency repos")]

    internal class CloneCommandLineOptions : CommandLineOptions
    {
        [Option("repo", Required = true, HelpText = "Remote repository to start the clone operation at.")]
        public string RepoUri { get; set; }

        [Option('v', "version", Required = true, HelpText = "Branch, commit or tag to start at in the remote repository.")]
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

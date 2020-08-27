// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("add-default-channel", HelpText = "Add a channel that a build of a branch+repository is automatically applied to.")]
    internal class AddDefaultChannelCommandLineOptions : CommandLineOptions
    {
        [Option("channel", Required = true, HelpText = "Name of channel that a build of 'branch' and 'repo' should be applied to.")]
        public string Channel { get; set; }

        [Option("branch", Required = true, HelpText = "Builds of 'repo' on this branch will be automatically applied to 'channel'.  Use with '--regex' to match on multiple branch names")]
        public string Branch { get; set; }

        [Option("repo", Required = true, HelpText = "Builds of this repo on 'branch' will be automatically applied to 'channel'")]
        public string Repository { get; set; }

        [Option("regex", Required = false, HelpText = "If specified, the value of the 'branch' option will be treated as a regular expression for matching branch names.", Default = false)]
        public bool UseBranchAsRegex { get; set; }

        [Option('q', "quiet", HelpText = "Do not prompt if the target repository/branch does not exist.")]
        public bool NoConfirmation { get; set; }

        public override Operation GetOperation()
        {
            return new AddDefaultChannelOperation(this);
        }
    }
}

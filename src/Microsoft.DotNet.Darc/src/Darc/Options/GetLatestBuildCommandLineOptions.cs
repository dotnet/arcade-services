// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("get-latest-build", HelpText = "Retrieves the latest build of a repository")]
    internal class GetLatestBuildCommandLineOptions : CommandLineOptions
    {
        [Option("repo", Required = true, HelpText = "Name of repository to determine the latest build for.")]
        public string Repo { get; set; }

        [Option("channel", HelpText = "Name of channel to query for the latest build on.")]
        public string Channel { get; set; }

        public override Operation GetOperation()
        {
            return new GetLatestBuildOperation(this);
        }
    }
}

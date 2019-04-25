// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;

namespace Microsoft.DotNet.Darc.Options
{
    abstract class UpdateDefaultChannelBaseCommandLineOptions : CommandLineOptions
    {
        [Option("id", Default = -1, HelpText = "Existing default channel id")]
        public int Id { get; set; }

        [Option("channel", HelpText = "Existing default channel association target channel name.")]
        public string Channel { get; set; }

        [Option("branch", HelpText = "Existing default channel association source branch name.")]
        public string Branch { get; set; }

        [Option("repo", HelpText = "Existing default channel association source repository name.")]
        public string Repository { get; set; }
    }
}

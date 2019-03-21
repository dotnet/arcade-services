// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("add-build-to-channel", HelpText = "Add a build to a channel.")]
    internal class AddBuildToChannelCommandLineOptions : CommandLineOptions
    {
        [Option("id", Required = true, HelpText = "BAR id of build to assign to channel.")]
        public int Id { get; set; }

        [Option("channel", Required = true, HelpText = "Channel to assign build to.")]
        public string Channel { get; set; }

        public override Operation GetOperation()
        {
            return new AddBuildToChannelOperation(this);
        }
    }
}

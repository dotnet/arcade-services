// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("delete-build-from-channel", HelpText = "Removes a build from a channel.")]
    internal class DeleteBuildFromChannelCommandLineOptions : CommandLineOptions
    {
        [Option("id", Required = true, HelpText = "BAR id of build to assign to channel.")]
        [RedactFromLogging]
        public int Id { get; set; }

        [Option("channel", Required = true, HelpText = "Channel to remove the build from.")]
        public string Channel { get; set; }

        public override Operation GetOperation()
        {
            return new DeleteBuildFromChannelOperation(this);
        }
    }
}

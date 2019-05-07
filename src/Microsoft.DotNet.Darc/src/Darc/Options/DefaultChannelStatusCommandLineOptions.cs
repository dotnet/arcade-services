// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using System.Text.RegularExpressions;
using System;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("default-channel-status", HelpText = "Enables or disables a default channel association")]
    internal class DefaultChannelStatusCommandLineOptions : UpdateDefaultChannelBaseCommandLineOptions
    {
        [Option('e', "enable", HelpText = "Enable default channel.")]
        public bool Enable { get; set; }

        [Option('d', "disable", HelpText = "Disable default channel.")]
        public bool Disable { get; set; }

        public override Operation GetOperation()
        {
            return new DefaultChannelStatusOperation(this);
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib;
using System.Collections.Generic;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("get-asset", HelpText = "Get information about an asset.")]
    internal class GetAssetCommandLineOptions : CommandLineOptions
    {
        [Option("name", Required = true, HelpText = "Name of asset to look up")]
        public string Name { get; set; }

        [Option("version", HelpText = "Look up specific version of an asset.")]
        public string Version { get; set; }

        [Option("channel", HelpText = "Look up the asset produced from builds applied to this channel.")]
        public string Channel { get; set; }

        [Option("max-age", Default = 30, HelpText = "Show builds with a max age of this many days.")]
        public int MaxAgeInDays { get; set; }

        public override Operation GetOperation()
        {
            return new GetAssetOperation(this);
        }
    }
}

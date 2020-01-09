// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("update-build", HelpText = "Update a build with new information.")]
    internal class UpdateBuildCommandLineOptions : CommandLineOptions
    {
        [Option("id", Required = true, HelpText = "Build id.")]
        public int Id { get; set; }

        [Option("released", HelpText = "Set the build to being 'released'.")]
        public bool Released { get; set; }

        [Option("not-released", HelpText = "Set the build to 'not released'.")]
        public bool NotReleased { get; set; }

        public override Operation GetOperation()
        {
            return new UpdateBuildOperation(this);
        }
    }
}

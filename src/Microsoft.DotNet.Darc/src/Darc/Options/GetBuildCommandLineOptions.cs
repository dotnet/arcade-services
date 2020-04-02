// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("get-build", HelpText = "Retrieves a specific build of a repository")]
    internal class GetBuildCommandLineOptions : CommandLineOptions
    {
        [Option("id", HelpText = "Build id.")]
        public int Id { get; set; }

        [Option("uri", HelpText = "Uri of the build.")]
        public string BuildUri { get; set; }

        [Option("repo", HelpText = "Repository")]
        public string Repo { get; set; }

        [Option("commit", HelpText = "Commit")]
        public string Commit { get; set; }

        public override Operation GetOperation()
        {
            return new GetBuildOperation(this);
        }
    }
}

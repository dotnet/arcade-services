// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    public abstract class CommandLineOptions
    {
        [Option('p', "password", HelpText = "BAR password.")]
        [RedactFromLogging]
        public string BuildAssetRegistryPassword { get; set; }

        [Option("github-pat", HelpText = "Token used to authenticate GitHub.")]
        [RedactFromLogging]
        public string GitHubPat { get; set; }

        [Option("azdev-pat", HelpText = "Token used to authenticate to Azure DevOps.")]
        [RedactFromLogging]
        public string AzureDevOpsPat { get; set; }

        [Option("bar-uri", HelpText = "URI of the build asset registry service to use.")]
        public string BuildAssetRegistryBaseUri { get; set; }

        [Option("verbose", HelpText = "Turn on verbose output.")]
        public bool Verbose { get; set; }

        [Option("debug", HelpText = "Turn on debug output.")]
        public bool Debug { get; set; }

        [Option("git-location", Default = "git", HelpText = "Location of git executable used for internal commands.")]
        [RedactFromLogging]
        public string GitLocation { get; set; }

        [Option("output-format", Default = DarcOutputType.text,
            HelpText = "Desired output type of darc. Valid values are 'json' and 'text'. Case sensitive.")]
        public DarcOutputType OutputFormat { get; set; }

        public abstract Operation GetOperation();
    }
}

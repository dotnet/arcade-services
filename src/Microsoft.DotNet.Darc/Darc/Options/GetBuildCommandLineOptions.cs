// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-build", HelpText = "Retrieves a specific build of a repository")]
internal class GetBuildCommandLineOptions : CommandLineOptions<GetBuildOperation>
{
    [Option("id", HelpText = "Build id.")]
    [RedactFromLogging]
    public int Id { get; set; }

    [Option("repo", HelpText = "Full url of the repository that was built, or match on substring")]
    public string Repo { get; set; }

    [Option("commit", HelpText = "Full commit sha that was built")]
    [RedactFromLogging]
    public string Commit { get; set; }

    [Option("extended", HelpText = "Show all available fields (applies to JSON output-format only)")]
    public bool ExtendedDetails { get; set; }

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}

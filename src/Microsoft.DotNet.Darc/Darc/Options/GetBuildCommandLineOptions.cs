// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetBuildCommandLineOptions : CommandLineOptions<GetBuildOperation>
{
    public override string Verb => "get-build";

    public override string VerbDescription => "Retrieves a specific build of a repository";

    [RedactFromLogging]
    public int Id { get; set; }

    public string Repo { get; set; }

    [RedactFromLogging]
    public string Commit { get; set; }

    public bool ExtendedDetails { get; set; }

    protected override void Configure()
    {
        Add<int>("--id", "Build id.", value => Id = value);
        Add<string>("--repo", "Full url of the repository that was built, or match on substring", value => Repo = value);
        Add<string>("--commit", "Full commit sha that was built", value => Commit = value);
        Add<bool>("--extended", "Show all available fields (applies to JSON output-format only)", value => ExtendedDetails = value);
        base.Configure();
    }

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}

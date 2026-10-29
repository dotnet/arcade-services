// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetDefaultChannelsCommandLineOptions : CommandLineOptions<GetDefaultChannelsOperation>
{
    public override string Verb => "get-default-channels";

    public override string VerbDescription => "Gets a list of repo+branch combinations and their associated default channels for builds.";

    public string SourceRepository { get; set; }

    public string Branch { get; set; }

    public string Channel { get; set; }

    public IEnumerable<string> Ids { get; set; }

    protected override void Configure()
    {
        Add<string>("--source-repo", "Filter by a specific source repository. Matches on substring.", value => SourceRepository = value);
        Add<string>("--branch", "Filter by a branch. Matches on substring.", value => Branch = value);
        Add<string>("--channel", "Filter by a channel name. Matches on substring.", value => Channel = value);
        AddSeparatedList(',', "--ids", "Get only default channels with these comma-separated IDs.", value => Ids = value);
        base.Configure();
    }

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}

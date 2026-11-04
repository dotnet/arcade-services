// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options;

internal class LoginCommandLineOptions : CommandLineOptions<LoginOperation>
{
    public override string Verb => "login";

    public override string VerbDescription => "Authenticate with Maestro (credentials are persisted for a time)";

    public string BarUri { get; set; }

    protected override void Configure()
    {
        Add<string>("--bar-uri", "URI of the Build Asset Registry service to authenticate with. Defaults to production Maestro", value => BarUri = value);
        base.Configure();
    }

    protected override LogLevel DefaultLogVerbosity => LogLevel.Information;
}

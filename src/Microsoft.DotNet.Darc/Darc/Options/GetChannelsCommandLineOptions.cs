// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-channels", HelpText = "Get a list of channels.")]
internal class GetChannelsCommandLineOptions : CommandLineOptions
{
    public override Operation GetOperation()
    {
        return new GetChannelsOperation(this);
    }
}

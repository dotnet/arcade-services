// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-channels", HelpText = "Get a list of channels.")]
internal class GetChannelsCommandLineOptions : CommandLineOptions
{
    public override Operation GetOperation(ServiceProvider sp) => ActivatorUtilities.CreateInstance<GetChannelsOperation>(sp, this);


    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}

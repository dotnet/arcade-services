// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("get-subscriptions", HelpText = "Get information about subscriptions.")]
internal class GetSubscriptionsCommandLineOptions : SubscriptionsCommandLineOptions<GetSubscriptionsOperation>
{
    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}

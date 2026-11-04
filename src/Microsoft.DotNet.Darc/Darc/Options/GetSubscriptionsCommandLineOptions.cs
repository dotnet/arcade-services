// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GetSubscriptionsCommandLineOptions : SubscriptionsCommandLineOptions<GetSubscriptionsOperation>
{
    public override string Verb => "get-subscriptions";

    public override string VerbDescription => "Get information about subscriptions.";

    public override bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(),
        };
}

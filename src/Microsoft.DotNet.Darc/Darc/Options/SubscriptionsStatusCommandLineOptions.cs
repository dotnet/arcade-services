// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class SubscriptionsStatusCommandLineOptions : SubscriptionsCommandLineOptions<SubscriptionsStatusOperation>
{
    public override string Verb => "subscription-status";

    public override string VerbDescription => "Enables or disables subscriptions matching a specified criteria.";

    public string Id { get; set; }

    public bool Enable { get; set; }

    public bool Disable { get; set; }

    public bool NoConfirmation { get; set; }

    protected override void Configure()
    {
        Add<string>("--id", "Specific subscription's id.", value => Id = value);
        Add<bool>("--enable", "-e", "Enable subscription(s).", value => Enable = value);
        Add<bool>("--disable", "-d", "Disable subscription(s).", value => Disable = value);
        Add<bool>("--quiet", "-q", "Do not confirm which subscriptions are about to be enabled/disabled.", value => NoConfirmation = value);
        base.Configure();
    }
}

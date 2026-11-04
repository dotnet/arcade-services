// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class TriggerSubscriptionsCommandLineOptions : SubscriptionsCommandLineOptions<TriggerSubscriptionsOperation>
{
    public override string Verb => "trigger-subscriptions";

    public override string VerbDescription => "Trigger a subscription or set of subscriptions matching criteria.";

    public string Id { get; set; }

    public int Build { get; set; }

    public bool NoConfirmation { get; set; }

    public bool Force { get; set; }

    public bool NoWait { get; set; }

    protected override void Configure()
    {
        Add<string>("--id", "Trigger a specific subscription by id.", value => Id = value);
        Add<int>("--build", "If specified, selects a specific BAR build id to use; otherwise will use the latest available from the supplied '--source-repo'", value => Build = value);
        Add<bool>("--quiet", "-q", "Do not confirm which subscriptions are about to be triggered.", value => NoConfirmation = value);
        Add<bool>("--force", "-f", "Force update of existing pull requests even if they have pending or successful checks.", value => Force = value);
        Add<bool>("--no-wait", "Do not wait for the latest trigger outcome after triggering.", value => NoWait = value);
        base.Configure();
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class DeleteSubscriptionsCommandLineOptions : SubscriptionsCommandLineOptions<DeleteSubscriptionsOperation>
{
    public override string Verb => "delete-subscriptions";

    public override string VerbDescription => "Delete a subscription or set of subscriptions matching criteria.";

    public string Id { get; set; }

    public bool NoConfirmation { get; set; }

    protected override void Configure()
    {
        Add<string>("--id", "Delete a specific subscription by id.", value => Id = value);
        Add<bool>("--quiet", "-q", "Do not confirm which subscriptions are about to be triggered.", value => NoConfirmation = value);
        base.Configure();
    }
}

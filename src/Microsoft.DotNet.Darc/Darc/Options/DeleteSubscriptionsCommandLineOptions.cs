// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options;

[Verb("delete-subscriptions", HelpText = "Delete a subscription or set of subscriptions matching criteria.")]
internal class DeleteSubscriptionsCommandLineOptions : SubscriptionsCommandLineOptions
{
    [Option("id", HelpText = "Delete a specific subscription by id.")]
    public string Id { get; set; }

    [Option('q', "quiet", HelpText = "Do not confirm which subscriptions are about to be triggered.")]
    public bool NoConfirmation { get; set; }

    public override Operation GetOperation(ServiceProvider sp) => ActivatorUtilities.CreateInstance<DeleteSubscriptionsOperation>(sp, this);

}

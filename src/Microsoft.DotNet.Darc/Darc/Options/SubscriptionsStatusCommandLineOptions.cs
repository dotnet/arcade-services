// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Options;

[Verb("subscription-status", HelpText = "Enables or disables subscriptions matching a specified criteria.")]
internal class SubscriptionsStatusCommandLineOptions : SubscriptionsCommandLineOptions
{
    [Option("id", HelpText = "Specific subscription's id.")]
    public string Id { get; set; }

    [Option('e', "enable", HelpText = "Enable subscription(s).")]
    public bool Enable { get; set; }

    [Option('d', "disable", HelpText = "Disable subscription(s).")]
    public bool Disable { get; set; }

    [Option('q', "quiet", HelpText = "Do not confirm which subscriptions are about to be enabled/disabled.")]
    public bool NoConfirmation { get; set; }

    public override Operation GetOperation(ServiceProvider sp) => ActivatorUtilities.CreateInstance<SubscriptionsStatusOperation>(sp, this);
}

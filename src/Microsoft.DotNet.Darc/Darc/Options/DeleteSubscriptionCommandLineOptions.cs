// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("delete-subscription", Hidden = true, HelpText = "Please use delete-subscriptions.")]
internal class DeleteSubscriptionCommandLineOptions : CommandLineOptions
{
    [Option('i', "id", Required = true, HelpText = "ID of subscription to delete.")]
    public string Id { get; set; }

    public override Operation GetOperation()
    {
        return new DeleteSubscriptionOperation(this);
    }
}

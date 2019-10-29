// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("update-subscription", HelpText = "Update an existing subscription.")]
    class UpdateSubscriptionCommandLineOptions : CommandLineOptions
    {
        [Option("id", Required = true, HelpText = "Subscription's id.")]
        public string Id { get; set; }

        [Option("trigger", SetName = "trigger", HelpText = "Automatically trigger the subscription on update.")]
        public bool TriggerOnUpdate { get; set; }

        [Option("no-trigger", SetName = "notrigger", HelpText = "Do not trigger the subscription on update.")]
        public bool NoTriggerOnUpdate { get; set; }

        public override Operation GetOperation()
        {
            return new UpdateSubscriptionOperation(this);
        }
    }
}

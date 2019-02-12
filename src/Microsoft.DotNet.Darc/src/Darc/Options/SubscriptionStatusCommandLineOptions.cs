// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using System.Text.RegularExpressions;
using System;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("subscription-status", HelpText = "Enables or disables a subscription matching the id.")]
    internal class SubscriptionStatusCommandLineOptions : CommandLineOptions
    {
        [Option("id", Required = true, HelpText = "Subscription's id.")]
        public string Id { get; set; }

        [Option('e', "enable", HelpText = "Enable subscription.")]
        public bool Enable { get; set; }

        [Option('d', "disable", HelpText = "Disable subscription.")]
        public bool Disable { get; set; }

        public override Operation GetOperation()
        {
            return new SubscriptionStatus(this);
        }
    }
}

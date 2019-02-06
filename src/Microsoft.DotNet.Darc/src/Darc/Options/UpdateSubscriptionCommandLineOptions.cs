// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using System.Collections.Generic;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("update-subscription", HelpText = "Update an existing subscription.")]
    class UpdateSubscriptionCommandLineOptions : CommandLineOptions
    {
        [Option("id", Required = true, HelpText = "Subscription's id.")]
        public string Id { get; set; }

        public override Operation GetOperation()
        {
            return new UpdateSubscriptionOperation(this);
        }
    }
}

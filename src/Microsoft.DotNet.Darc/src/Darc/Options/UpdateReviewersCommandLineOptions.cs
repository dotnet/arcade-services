// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("update-reviewers", HelpText = "Update reviewers for all pull requests created by an existing subscription.")]
    internal class UpdateReviewersCommandLineOptions : CommandLineOptions
    {
        [Option("subscription-id", HelpText = "Id of the subscription to update.")]
        public string SubscriptionId { get; set; }

        [Option("reviewers", HelpText = "User names of the reviewers, comma separated. " +
            "Note that this overwrites the entire reviewers list for this subscription")]
        public string Reviewers { get; set; }

        public override Operation GetOperation()
        {
            return new UpdateReviewersOperation(this);
        }
    }
}

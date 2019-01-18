// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("trigger-subscriptions", HelpText = "Trigger a subscription or set of subscriptions matching criteria.")]
    internal class TriggerSubscriptionsCommandLineOptions : CommandLineOptions
    {
        [Option("id", HelpText = "Trigger a specific subscription by id.")]
        public string Id { get; set; }

        [Option('q', "quiet", HelpText = "Do not confirm which subscriptions are about to be triggered.")]
        public bool NoConfirmation { get; set; }

        [Option("target-repo", HelpText = "Filter by target repo (matches substring).")]
        public string TargetRepository { get; set; }

        [Option("source-repo", HelpText = "Filter by source repo (matches substring).")]
        public string SourceRepository { get; set; }

        [Option("channel", HelpText = "Filter by source channel (matches substring).")]
        public string Channel { get; set; }

        [Option("target-branch", HelpText = "Filter by target branch (matches substring).")]
        public string TargetBranch { get; set; }

        public override Operation GetOperation()
        {
            return new TriggerSubscriptionsOperation(this);
        }
    }
}

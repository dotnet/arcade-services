// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("update-subscription", HelpText = "Update an existing subscription. If no arguments beyond '--id' are specified, a text editor is popped up with the current values for the subscription.  (As they are specified via YAML, merge policy settings must use the editor)")]
class UpdateSubscriptionCommandLineOptions : CommandLineOptions
{
    [Option("id", Required = true, HelpText = "Subscription's id.")]
    public string Id { get; set; }

    [Option("trigger", SetName = "trigger", HelpText = "Automatically trigger the subscription on update.")]
    public bool TriggerOnUpdate { get; set; }

    [Option("no-trigger", SetName = "notrigger", HelpText = "Do not trigger the subscription on update.")]
    public bool NoTriggerOnUpdate { get; set; }

    [Option("channel", HelpText = "Target channel of the the subscription to be updated")]
    public string Channel { get; set; }

    [Option("source-repository-url", HelpText = "Source repository's URL of the subscription to be updated")]
    public string SourceRepoUrl { get; set; }

    [Option("batchable", HelpText = "Whether this subscription's content can be updated in batches. Not supported when the subscription specifies merge policies")]
    public bool? Batchable { get; set; }

    [Option("update-frequency", HelpText = "How often subscription updates should occur.")]
    public string UpdateFrequency { get; set; }

    [Option("enabled", HelpText = "Whether subscription is enabled (active) or not")]
    public bool? Enabled { get; set; }

    [Option("failure-notification-tags", HelpText = "Semicolon-delineated list of GitHub tags to notify for dependency flow failures from this subscription")]
    public string FailureNotificationTags { get; set; }

    public override Operation GetOperation()
    {
        return new UpdateSubscriptionOperation(this);
    }
}

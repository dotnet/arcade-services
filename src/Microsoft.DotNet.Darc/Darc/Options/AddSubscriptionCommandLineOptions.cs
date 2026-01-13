// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("add-subscription", HelpText = "Add a new subscription.")]
internal class AddSubscriptionCommandLineOptions : SubscriptionCommandLineOptions<AddSubscriptionOperation>
{
    [Option("enabled", HelpText = "Whether subscription is enabled (active) or not", Default = true)]
    public bool Enabled { get; set; }

    [Option("channel", HelpText = "Name of channel to pull from.")]
    public string Channel { get; set; }

    [Option("source-repo", HelpText = "Source repository for the subscription.")]
    public string SourceRepository { get; set; }

    [Option("target-repo", HelpText = "Target repository for the subscription.")]
    public string TargetRepository { get; set; }

    [Option("target-branch", HelpText = "Target branch for the subscription.")]
    public string TargetBranch { get; set; }

    [Option("batchable", HelpText = "Whether this subscription's content can be updated in batches. Not supported when the subscription specifies merge policies or is a codeflow subscription (source-enabled).")]
    public bool Batchable { get; set; }

    [Option('q', "quiet", HelpText = "Non-interactive mode (requires all elements to be passed on the command line).")]
    public bool Quiet { get; set; }

    [Option("read-stdin", HelpText = "Interactive mode style (YAML), but read input from stdin. Implies -q")]
    public bool ReadStandardIn { get; set; }

    [Option("trigger", SetName = "trigger", HelpText = "Automatically trigger the subscription on creation.")]
    public bool TriggerOnCreate { get; set; }

    [Option("no-trigger", SetName = "notrigger", HelpText = "Do not trigger the subscription on creation.")]
    public bool NoTriggerOnCreate { get; set; }

    [Option("source-enabled", HelpText = "Get only source-enabled (VMR code flow) subscriptions.", Default = false)]
    public bool SourceEnabled { get; set; }

    [Option("subscription", HelpText = "GUID of an existing subscription to copy settings from. Other command-line parameters will override the copied values. Note: Boolean flags (enabled, batchable, source-enabled) are copied when no merge policies are specified via command line.")]
    public string CopyFromSubscription { get; set; }
}

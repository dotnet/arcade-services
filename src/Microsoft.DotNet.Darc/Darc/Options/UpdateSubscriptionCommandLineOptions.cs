// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("update-subscription", HelpText = "Update an existing subscription. If no arguments beyond '--id' are specified, a text editor is popped up with the current values for the subscription.  (As they are specified via YAML, merge policy settings must use the editor)")]
internal class UpdateSubscriptionCommandLineOptions : SubscriptionCommandLineOptions<UpdateSubscriptionOperation>
{
    [Option("id", Required = true, HelpText = "Subscription's id.")]
    public string Id { get; set; }

    [Option("channel", HelpText = "Target channel of the the subscription to be updated")]
    public string Channel { get; set; }

    [Option("source-repository-url", HelpText = "Source repository's URL of the subscription to be updated")]
    public string SourceRepoUrl { get; set; }

    [Option("batchable", HelpText = "Whether this subscription's content can be updated in batches. Not supported when the subscription specifies merge policies or is a codeflow subscription (source-enabled).")]
    public bool? Batchable { get; set; }

    [Option("enabled", HelpText = "Whether subscription is enabled (active) or not")]
    public bool? Enabled { get; set; }

    [Option("source-enabled", HelpText = "Get only source-enabled (VMR code flow) subscriptions.")]
    public bool? SourceEnabled { get; set; }

    [Option("update-merge-policies", Default = false, HelpText = "By default, if any merge policies are specific in the command, we'll overwrite the old ones. " +
                                                "This flag makes it so we add onto the previous ones, instead of overwriting")]
    public bool UpdateMergePolicies { get; set; }
}

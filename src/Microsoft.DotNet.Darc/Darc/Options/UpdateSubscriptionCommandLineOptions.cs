// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("update-subscription", HelpText = "Update an existing subscription. If no arguments beyond '--id' are specified, a text editor is opened with the current values.")]
internal class UpdateSubscriptionCommandLineOptions : SubscriptionCommandLineOptions<UpdateSubscriptionOperation>
{
    [Option("id", Required = true, HelpText = "Subscription's id.")]
    public string Id { get; set; }

    [Option("channel", HelpText = "Target channel of the the subscription to be updated")]
    public string Channel { get; set; }

    [Option("source-repository-url", HelpText = "Source repository's URL of the subscription to be updated")]
    public string SourceRepoUrl { get; set; }

    [Option("batchable", HelpText = "Whether this subscription's content can be updated in batches. Not supported with --merge-prs or for codeflow subscriptions (source-enabled).")]
    public bool? Batchable { get; set; }

    [Option("merge-prs", HelpText = "Whether Maestro should merge pull requests after all Maestro checks pass.")]
    public bool? MergePrs { get; set; }

    [Option("enabled", HelpText = "Whether subscription is enabled (active) or not")]
    public bool? Enabled { get; set; }

    [Option("source-enabled", HelpText = "Get only source-enabled (VMR code flow) subscriptions.")]
    public bool? SourceEnabled { get; set; }

    [Option("auto-approve", HelpText = "Whether pull requests should be automatically approved. Only allowed on forward flow subscriptions.")]
    public bool? AutoApprove { get; set; }

}

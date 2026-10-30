// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class UpdateSubscriptionCommandLineOptions : SubscriptionCommandLineOptions<UpdateSubscriptionOperation>
{
    public override string Verb => "update-subscription";

    public override string VerbDescription => "Update an existing subscription. If no arguments beyond '--id' are specified, a text editor is opened with the current values.";

    public string Id { get; set; }

    public string Channel { get; set; }

    public string SourceRepoUrl { get; set; }

    public bool? Batchable { get; set; }

    public bool? MergePrs { get; set; }

    public bool? Enabled { get; set; }

    public bool? SourceEnabled { get; set; }

    public bool? AutoApprove { get; set; }

    protected override void Configure()
    {
        Add<string>("--id", "Subscription's id.", value => Id = value, required: true);
        Add<string>("--channel", "Target channel of the the subscription to be updated", value => Channel = value);
        Add<string>("--source-repository-url", "Source repository's URL of the subscription to be updated", value => SourceRepoUrl = value);
        Add<bool?>("--batchable", "Whether this subscription's content can be updated in batches. Not supported with --merge-prs or for codeflow subscriptions (source-enabled).", value => Batchable = value);
        Add<bool?>("--merge-prs", "Whether Maestro should merge pull requests after all Maestro checks pass.", value => MergePrs = value);
        Add<bool?>("--enabled", "Whether subscription is enabled (active) or not", value => Enabled = value);
        Add<bool?>("--source-enabled", "Get only source-enabled (VMR code flow) subscriptions.", value => SourceEnabled = value);
        Add<bool?>("--auto-approve", "Whether pull requests should be automatically approved. Only allowed on forward flow subscriptions.", value => AutoApprove = value);
        base.Configure();
    }
}

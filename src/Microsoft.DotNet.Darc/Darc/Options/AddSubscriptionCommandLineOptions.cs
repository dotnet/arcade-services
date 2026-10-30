// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class AddSubscriptionCommandLineOptions : SubscriptionCommandLineOptions<AddSubscriptionOperation>
{
    public override string Verb => "add-subscription";

    public override string VerbDescription => "Add a new subscription.";

    public bool Enabled { get; set; }

    public string Channel { get; set; }

    public string SourceRepository { get; set; }

    public string TargetRepository { get; set; }

    public string TargetBranch { get; set; }

    public bool Batchable { get; set; }

    public bool MergePrs { get; set; }

    public bool Quiet { get; set; }

    public bool ReadStandardIn { get; set; }

    public bool SourceEnabled { get; set; }

    public bool AutoApprove { get; set; }

    public string CopyFromSubscription { get; set; }

    protected override void Configure()
    {
        Add<bool>("--enabled", "Whether subscription is enabled (active) or not", value => Enabled = value, defaultValue: () => true);
        Add<string>("--channel", "Name of channel to pull from.", value => Channel = value);
        Add<string>("--source-repo", "Source repository for the subscription.", value => SourceRepository = value);
        Add<string>("--target-repo", "Target repository for the subscription.", value => TargetRepository = value);
        Add<string>("--target-branch", "Target branch for the subscription.", value => TargetBranch = value);
        Add<bool>("--batchable", "Whether this subscription's content can be updated in batches. Not supported with --merge-prs or for codeflow subscriptions (source-enabled).", value => Batchable = value);
        Add<bool>("--merge-prs", "Whether Maestro should merge pull requests after all Maestro checks pass.", value => MergePrs = value);
        Add<bool>("--quiet", "-q", "Non-interactive mode (requires all elements to be passed on the command line).", value => Quiet = value);
        Add<bool>("--read-stdin", "Interactive mode style (YAML), but read input from stdin. Implies -q", value => ReadStandardIn = value);
        Add<bool>("--source-enabled", "Get only source-enabled (VMR code flow) subscriptions.", value => SourceEnabled = value, defaultValue: () => false);
        Add<bool>("--auto-approve", "Whether passing pull requests should be automatically approved. Only allowed on forward flow subscriptions.", value => AutoApprove = value, defaultValue: () => false);
        Add<string>("--subscription", "GUID of an existing subscription to copy settings from. Other command-line parameters will override the copied values. Note: Boolean flags are copied when no Merge PRs settings are specified via command line.", value => CopyFromSubscription = value);
        base.Configure();
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("update-subscription", HelpText = "Update an existing subscription. If no arguments beyond '--id' are specified, a text editor is popped up with the current values for the subscription.  (As they are specified via YAML, merge policy settings must use the editor)")]
internal class UpdateSubscriptionCommandLineOptions : SubscriptionCommandLineOptions<UpdateSubscriptionOperation>
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

    [Option("enabled", HelpText = "Whether subscription is enabled (active) or not")]
    public bool? Enabled { get; set; }

    [Option("source-enabled", HelpText = "Get only source-enabled (VMR code flow) subscriptions.")]
    public bool? SourceEnabled { get; set; }

    [Option("standard-automerge", HelpText = "Use standard auto-merge policies. GitHub ignores WIP, license/cla and auto-merge.config.enforce checks, Azure DevOps ignores comment, reviewer and work item linking. Both will not auto-merge if changes are requested.")]
    public bool StandardAutoMergePolicies { get; set; }

    [Option("all-checks-passed", HelpText = "PR is automatically merged if there is at least one check, and all checks have passed. Optionally provide a comma-separated list of ignored check with --ignore-checks.")]
    public bool AllChecksSuccessfulMergePolicy { get; set; }

    [Option("ignore-checks", Separator = ',', HelpText = "For use with --all-checks-passed. A set of checks that are ignored.")]
    public IEnumerable<string> IgnoreChecks { get; set; }

    [Option("no-requested-changes", HelpText = "PR is not merged if there are changes requested or the PR has been rejected.")]
    public bool NoRequestedChangesMergePolicy { get; set; }

    [Option("no-downgrades", HelpText = "PR is not merged if there are version downgrades.")]
    public bool DontAutomergeDowngradesMergePolicy { get; set; }

    [Option("validate-coherency", HelpText = "PR is not merged if the coherency algorithm failed.")]
    public bool ValidateCoherencyCheckMergePolicy { get; set; }

    [Option("overwrite-merge-policies", HelpText = "Overwrite the merge policies of the subscription. If not specified, the merge policies are appended to the existing ones.")]
    public bool OverwriteMergePolicies { get; set; }
}

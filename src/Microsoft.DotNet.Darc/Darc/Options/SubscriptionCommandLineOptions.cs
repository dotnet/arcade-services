﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal abstract class SubscriptionCommandLineOptions<T> : CommandLineOptions<T> where T : Operation
{
    [Option("update-frequency", HelpText = "Frequency of updates. Valid values are: 'none', 'everyDay', 'everyBuild', 'twiceDaily', or 'everyWeek'.")]
    public string UpdateFrequency { get; set; }

    [Option("failure-notification-tags", HelpText = "Semicolon-delineated list of GitHub tags to notify for dependency flow failures from this subscription")]
    public string FailureNotificationTags { get; set; }

    [Option("source-directory", HelpText = "Name of the VMR source directory which are the repository sources synchronized from.")]
    public string SourceDirectory { get; set; }

    [Option("target-directory", HelpText = "Name of the VMR target directory which are the repository sources synchronized to.")]
    public string TargetDirectory { get; set; }

    [Option("excluded-assets", HelpText = "Semicolon-delineated list of asset filters (package name with asterisks allowed) to be excluded from source-enabled code flow.")]
    public string ExcludedAssets { get; set; }

    [Option('f', "force", HelpText = "Force subscription creation even when some checks fail.")]
    public bool ForceCreation { get; set; }

    [Option("standard-automerge", HelpText = "Use standard auto-merge policies. GitHub ignores WIP, license/cla and auto-merge.config.enforce checks, " +
                                             "Azure DevOps ignores comment, reviewer and work item linking. Both will not auto-merge if changes are requested. " +
                                             "Also adds the Source Flow Check policy for source enabled subscriptions")]
    public bool StandardAutoMergePolicies { get; set; }

    [Option("all-checks-passed", HelpText = "PR is automatically merged if there is at least one check, and all checks have passed. " +
                                            "Optionally provide a comma-separated list of ignored check with --ignore-checks.")]
    public bool AllChecksSuccessfulMergePolicy { get; set; }

    [Option("ignore-checks", Separator = ',', HelpText = "For use with --all-checks-passed or --standard-automerge. A set of checks that are ignored. " +
                                                         "If used with standard-automerge, ignored checks will be added to the list of default ones")]
    public IEnumerable<string> IgnoreChecks { get; set; }

    [Option("no-requested-changes", HelpText = "PR is not merged if there are changes requested or the PR has been rejected.")]
    public bool NoRequestedChangesMergePolicy { get; set; }

    [Option("no-downgrades", HelpText = "PR is not merged if there are version downgrades.")]
    public bool DontAutomergeDowngradesMergePolicy { get; set; }

    [Option("code-flow-check", HelpText = "PR is not merged if the ForwardFlow/BackFlow check fails for ForwardFlow/BackwardFlow subscription")]
    public bool CodeFlowCheckMergePolicy { get; set; }

    [Option("validate-coherency", HelpText = "PR is not merged if the coherency algorithm failed.")]
    public bool ValidateCoherencyCheckMergePolicy { get; set; }
}

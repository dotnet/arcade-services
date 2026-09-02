// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal abstract class SubscriptionCommandLineOptions<T> : ConfigurationManagementCommandLineOptions<T> where T : Operation
{
    [Option("update-frequency", HelpText = "Frequency of updates. Valid values are: 'none', 'everyDay', 'everyBuild', 'twiceDaily', 'everyWeek', 'everyTwoWeeks', or 'everyMonth'.")]
    public string UpdateFrequency { get; set; }

    [Option("failure-notification-tags", HelpText = "Semicolon-delineated list of GitHub tags to notify for dependency flow failures from this subscription")]
    public string FailureNotificationTags { get; set; }

    [Option("source-directory", HelpText = "Name of the VMR source directory which are the repository sources synchronized from. Only supported for source enabled subscriptions")]
    public string SourceDirectory { get; set; }

    [Option("target-directory", HelpText = "In source enabled subs: Name of the VMR target directory which are the repository sources synchronized to." +
        " In dependency flow subscriptions: Comma separated list of paths ('.' or '/' for repo root) where the dependency updates are applied." +
        " These paths support globbing, but only at the end of the path, e.g src/*")]
    public string TargetDirectory { get; set; }

    [Option("excluded-assets", HelpText = "Semicolon-delineated list of asset filters (package name with asterisks allowed) to be excluded." +
        " When used with dependency flow subscriptions with specified target directories, it is possible to exclude assets in specific directories" +
        " e.g. - src/sdk/System.Text.json, or use globbing e.g. - src/*/System.Text.* ")]
    public string ExcludedAssets { get; set; }

    [Option('f', "force", HelpText = "Force subscription creation even when some checks fail.")]
    public bool ForceCreation { get; set; }

    [Option("ignore-checks", Separator = ',', HelpText = "A comma-separated list of checks ignored when merging pull requests. Requires --merge-prs.")]
    public IEnumerable<string> IgnoreChecks { get; set; } = [];
}

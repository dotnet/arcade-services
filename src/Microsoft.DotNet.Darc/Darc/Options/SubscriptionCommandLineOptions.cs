// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal abstract class SubscriptionCommandLineOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T> : ConfigurationManagementCommandLineOptions<T> where T : Operation
{
    public string UpdateFrequency { get; set; }

    public string FailureNotificationTags { get; set; }

    public string SourceDirectory { get; set; }

    public string TargetDirectory { get; set; }

    public string ExcludedAssets { get; set; }

    public bool ForceCreation { get; set; }

    public IEnumerable<string> IgnoredChecks { get; set; } = [];

    protected override void Configure()
    {
        Add<string>("--update-frequency", "Frequency of updates. Valid values are: 'none', 'everyDay', 'everyBuild', 'twiceDaily', 'everyWeek', 'everyTwoWeeks', or 'everyMonth'.", value => UpdateFrequency = value);
        Add<string>("--failure-notification-tags", "Semicolon-delineated list of GitHub tags to notify for dependency flow failures from this subscription", value => FailureNotificationTags = value);
        Add<string>("--source-directory", "Name of the VMR source directory which are the repository sources synchronized from. Only supported for source enabled subscriptions", value => SourceDirectory = value);
        Add<string>(
            "--target-directory",
            "In source enabled subs: Name of the VMR target directory which are the repository sources synchronized to." +
                " In dependency flow subscriptions: Comma separated list of paths ('.' or '/' for repo root) where the dependency updates are applied." +
                " These paths support globbing, but only at the end of the path, e.g src/*",
            value => TargetDirectory = value);
        Add<string>(
            "--excluded-assets",
            "Semicolon-delineated list of asset filters (package name with asterisks allowed) to be excluded." +
                " When used with dependency flow subscriptions with specified target directories, it is possible to exclude assets in specific directories" +
                " e.g. - src/sdk/System.Text.json, or use globbing e.g. - src/*/System.Text.* ",
            value => ExcludedAssets = value);
        Add<bool>("--force", "-f", "Force subscription creation even when some checks fail.", value => ForceCreation = value);
        AddSeparatedList(',', "--ignore-checks", "A comma-separated list of checks ignored when evaluating pull requests.", value => IgnoredChecks = value);
        base.Configure();
    }
}

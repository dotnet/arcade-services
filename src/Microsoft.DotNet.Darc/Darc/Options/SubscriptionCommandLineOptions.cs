// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;

namespace Microsoft.DotNet.Darc.Options;

internal abstract class SubscriptionCommandLineOptions : CommandLineOptions
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
}

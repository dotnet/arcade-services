// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

[Verb("update-dependencies", HelpText = "Update local dependencies from a channel, build or local list of packages.")]
internal class UpdateDependenciesCommandLineOptions : CommandLineOptions<UpdateDependenciesOperation>
{
    [Option("id", HelpText = "Optional BAR id of build to be used instead of the latest build in the channel.")]
    [RedactFromLogging]
    public int BARBuildId { get; set; }

    [Option('c', "channel", HelpText = "Channel to pull dependencies from.")]
    public string Channel { get; set; }

    [Option("subscription", HelpText = "Subscription ID to simulate. When provided, updates dependencies as the specified subscription would.")]
    public string SubscriptionId { get; set; }

    [Option('n', "name", HelpText = "Optional name of dependency to update. Otherwise all " +
                                    "dependencies existing on 'channel' are updated.")]
    public string Name { get; set; }

    [Option('v', "version", HelpText = "The new version of dependency --name.")]
    public string Version { get; set; }

    [Option("source-repo", HelpText = "Only update dependencies whose source uri contains this string.")]
    public string SourceRepository { get; set; }

    [Option("packages-folder", HelpText = "An optional path to a folder which contains the NuGet " +
                                          "packages whose versions will be used to update existing dependencies.")]
    [RedactFromLogging]
    public string PackagesFolder { get; set; }

    [Option("dry-run", HelpText = "Show what will be updated, but make no changes.")]
    public bool DryRun { get; set; }

    [Option("coherency-only", HelpText = "Only do coherency updates.")]
    public bool CoherencyOnly { get; set; }

    [Option("no-coherency-updates", HelpText = "Skip coherency updates and only update dependencies from the given build.")]
    public bool NoCoherencyUpdates { get; set; }

    [Option("target-directory", HelpText = "In source enabled subs: Name of the VMR target directory which are the repository sources synchronized to." +
        " In dependency flow subscriptions: Comma separated list of paths ('.' or '/' for repo root) where the dependency updates are applied." +
        " These paths support globbing, but only at the end of the path, e.g src/*")]
    public string TargetDirectory { get; set; }

    [Option("excluded-assets", HelpText = "Semicolon-delineated list of asset filters (package name with asterisks allowed) to be excluded." +
        " When used with dependency flow subscriptions with specified target directories, it is possible to exclude assets in specific directories" +
        " e.g. src/sdk/System.Text.json or src/*/System.Text.* ")]
    public string ExcludedAssets { get; set; }
}

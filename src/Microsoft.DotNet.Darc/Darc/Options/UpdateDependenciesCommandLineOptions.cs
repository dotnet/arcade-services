// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class UpdateDependenciesCommandLineOptions : CommandLineOptions<UpdateDependenciesOperation>
{
    public override string Verb => "update-dependencies";

    public override string VerbDescription => "Update local dependencies from a channel, build or local list of packages.";

    [RedactFromLogging]
    public int BARBuildId { get; set; }

    public string Channel { get; set; }

    public string SubscriptionId { get; set; }

    public string Name { get; set; }

    public string Version { get; set; }

    public string SourceRepository { get; set; }

    [RedactFromLogging]
    public string PackagesFolder { get; set; }

    public bool DryRun { get; set; }

    public bool CoherencyOnly { get; set; }

    public bool NoCoherencyUpdates { get; set; }

    public string TargetDirectory { get; set; }

    public string ExcludedAssets { get; set; }

    public string ExcludedRepoOrigins { get; set; }

    protected override void Configure()
    {
        Add<int>("--id", "Optional BAR id of build to be used instead of the latest build in the channel.", value => BARBuildId = value);
        Add<string>("--channel", "-c", "Channel to pull dependencies from.", value => Channel = value);
        Add<string>("--subscription", "Subscription ID to simulate. When provided, updates dependencies as the specified subscription would.", value => SubscriptionId = value);
        Add<string>(
            "--name",
            "-n",
            "Optional name of dependency to update. Otherwise all " +
                "dependencies existing on 'channel' are updated.",
            value => Name = value);
        Add<string>("--version", "-v", "The new version of dependency --name.", value => Version = value);
        Add<string>("--source-repo", "Only update dependencies whose source uri contains this string.", value => SourceRepository = value);
        Add<string>(
            "--packages-folder",
            "An optional path to a folder which contains the NuGet " +
                "packages whose versions will be used to update existing dependencies.",
            value => PackagesFolder = value);
        Add<bool>("--dry-run", "Show what will be updated, but make no changes.", value => DryRun = value);
        Add<bool>("--coherency-only", "Only do coherency updates.", value => CoherencyOnly = value);
        Add<bool>("--no-coherency-updates", "Skip coherency updates and only update dependencies from the given build.", value => NoCoherencyUpdates = value);
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
                " e.g. src/sdk/System.Text.json or src/*/System.Text.* ",
            value => ExcludedAssets = value);
        Add<string>(
            "--excluded-repo-origins",
            "Semicolon-delineated list of repo origins to exclude assets from." +
                " Only applies when using a BAR build (--id or --channel). Repo origins are specified in the MergedManifest.xml of VMR builds.",
            value => ExcludedRepoOrigins = value);
        base.Configure();
    }
}

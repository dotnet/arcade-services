// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.Darc.Operations;

namespace Microsoft.DotNet.Darc.Options;

internal class GatherDropCommandLineOptions : CommandLineOptions<GatherDropOperation>
{
    public override string Verb => "gather-drop";

    public override string VerbDescription => "Gather a drop of the outputs for a build";

    [RedactFromLogging]
    public IEnumerable<int> RootBuildIds { get; set; }

    public string RepoUri { get; set; }

    [RedactFromLogging]
    public string Commit { get; set; }

    [RedactFromLogging]
    public string OutputDirectory { get; set; }

    public bool UseRelativePathsInManifest { get; set; }

    public int MaxConcurrentDownloads { get; set; }

    public int AssetDownloadTimeoutInSeconds { get; set; }

    public bool Transitive { get; set; }

    public string ReleaseName { get; set; }

    public bool ContinueOnError { get; set; }

    public bool IncludeNonShipping { get; set; }

    public bool Overwrite { get; set; }

    public bool DryRun { get; set; }

    public bool IncludeToolset { get; set; }

    public string Channel { get; set; }

    public bool NoWorkarounds { get; set; }

    public bool SkipExisting { get; set; }

    public bool IncludeReleased { get; set; }

    public bool Separated { get; set; }

    public bool LatestLocation { get; set; }

    [RedactFromLogging]
    public IEnumerable<string> SASSuffixes { get; set; }

    public bool UseAzureCredentialForBlobs { get; set; }

    public string AlwaysDownloadAssetPatterns { get; set; } = "";

    public string AssetFilter { get; set; }

    protected override void Configure()
    {
        AddSeparatedIntList(
            ',',
            "--id",
            "BAR ID(s) of the root build(s) that we want to gather. comma separated.",
            value => RootBuildIds = value,
            alias: "-i");
        Add<string>("--repo", "-r", "Gather a build drop for a build of this repo. Requires --commit or --channel.", value => RepoUri = value);
        Add<string>("--commit", "-c", "Commit to gather a drop for.", value => Commit = value);
        Add<string>("--output-dir", "-o", "Output directory to place build drop.", value => OutputDirectory = value, required: true);
        Add<bool>("--use-relative-paths", "If true, make all paths in the resultant manifest relative to the value of output-dir", value => UseRelativePathsInManifest = value, defaultValue: () => false);
        Add<int>("--max-downloads", "Maximum concurrent downloads.", value => MaxConcurrentDownloads = value, defaultValue: () => 4);
        Add<int>("--download-timeout", "Timeout in seconds for downloading each asset.", value => AssetDownloadTimeoutInSeconds = value, defaultValue: () => 400);
        Add<bool>("--full", "-f", "Gather the full drop (build and all input builds).", value => Transitive = value);
        Add<string>("--release-name", "Name of release to use when generating release json.", value => ReleaseName = value, defaultValue: () => "3.0.0-previewN");
        Add<bool>("--continue-on-error", "Continue on error rather than halting.", value => ContinueOnError = value);
        Add<bool>("--non-shipping", "Include non-shipping assets.", value => IncludeNonShipping = value);
        Add<bool>("--overwrite", "Overwrite existing files at the destination.", value => Overwrite = value);
        Add<bool>("--dry-run", "Do not actually download files, but print what we would do.", value => DryRun = value);
        Add<bool>("--include-toolset", "Include toolset dependencies.", value => IncludeToolset = value);
        Add<string>("--channel", "Download the latest from this channel. Matched on substring.", value => Channel = value);
        Add<bool>("--no-workarounds", "Do not allow workarounds when gathering the drop.", value => NoWorkarounds = value);
        Add<bool>("--skip-existing", "Skip files that already exist at the destination.", value => SkipExisting = value);
        Add<bool>("--include-released", "Include builds that are marked as released", value => IncludeReleased = value);
        Add<bool>("--separated", "Also download files to their repo separated locations", value => Separated = value);
        Add<bool>("--latest-location", "Download assets from their latest known location.", value => LatestLocation = value);
        AddSeparatedList(
            ',',
            "--sas-suffixes",
            "List of potential uri suffixes that can be used if anonymous " +
            "access to a blob uri fails. Appended directly to the end of the URI (use full SAS suffix starting with '?'.",
            value => SASSuffixes = value);
        Add<bool>("--use-azure-credential-for-blobs", "Use AzCliCredential to acquire token for downloading assets from Blob storages", value => UseAzureCredentialForBlobs = value);
        Add<string>("--always-download-asset-filters", "Comma-separated list of exact names or regexes which will always be downloaded. If not part of the usual payload, they will be downloaded to an 'extra-assets' folder.", value => AlwaysDownloadAssetPatterns = value);
        Add<string>("--asset-filter", "Only download assets matching the given regex filter", value => AssetFilter = value);
        base.Configure();
    }
}

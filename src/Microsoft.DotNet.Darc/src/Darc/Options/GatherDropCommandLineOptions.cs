// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommandLine;
using Microsoft.DotNet.Darc.Operations;
using System.Collections.Generic;

namespace Microsoft.DotNet.Darc.Options
{
    [Verb("gather-drop", HelpText = "Gather a drop of the outputs for a build")]
    internal class GatherDropCommandLineOptions : CommandLineOptions
    {
        [Option('i', "id", HelpText = "BAR ID of build.")]
        public int RootBuildId { get; set; }

        [Option('r', "repo", HelpText = "Gather a build drop for a build of this repo. Requires --commit or --channel.")]
        public string RepoUri { get; set; }

        [Option('c', "commit", HelpText = "Commit to gather a drop for.")]
        public string Commit { get; set; }

        [Option('o',"output-dir", Required = true, HelpText = "Output directory to place build drop.")]
        public string OutputDirectory { get; set; }

        [Option("max-downloads", Default = 4, HelpText = "Maximum concurrent downloads.")]
        public int MaxConcurrentDownloads { get; set; }

        [Option("download-timeout", Default = 30, HelpText = "Timeout in seconds for downloading each asset.")]
        public int AssetDownloadTimeoutInSeconds { get; set; }

        [Option('f', "full", HelpText = "Gather the full drop (build and all input builds).")]
        public bool Transitive { get; set; }

        [Option("release", HelpText = "Generate a layout compatible with releasing the product (separated drop, generate release json).")]
        public bool ReleaseLayout { get; set; }

        [Option("release-name", Default ="3.0.0-previewN", HelpText = "Name of release to use when generating release json.")]
        public string ReleaseName { get; set; }

        [Option("continue-on-error", HelpText = "Continue on error rather than halting.")]
        public bool ContinueOnError { get; set; }

        [Option("non-shipping", HelpText = "Include non-shipping assets.")]
        public bool IncludeNonShipping { get; set; }

        [Option("overwrite", HelpText = "Overwrite existing files at the destination.")]
        public bool Overwrite { get; set; }

        [Option("dry-run", HelpText = "Do not actually download files, but print what we would do.")]
        public bool DryRun { get; set; }

        [Option("include-toolset", HelpText = "Include toolset dependencies.")]
        public bool IncludeToolset { get; set; }

        [Option("sdk", HelpText = "Download an SDK (core-sdk). Requires --commit or --channel.")]
        public bool DownloadSdk { get; set; }

        [Option("runtime", HelpText = "Download a runtime (core-setup). Requires --commit or --channel.")]
        public bool DownloadRuntime { get; set; }

        [Option("aspnet", HelpText = "Download an aspnet drop (aspnet). Requires --commit or --channel.")]
        public bool DownloadAspNet { get; set; }

        [Option("channel", HelpText = "Download the latest from this channel. Matched on substring.")]
        public string Channel { get; set; }

        [Option("no-workarounds", HelpText = "Do not allow workarounds when gathering the drop.")]
        public bool NoWorkarounds { get; set; }

        [Option("skip-existing", HelpText = "Skip files that already exist at the destination.")]
        public bool SkipExisting { get; set; }

        [Option("skip-released", HelpText = "Skip builds that are marked as released")]
        public bool SkipReleased { get; set; }

        [Option("latest-location", HelpText = "Download assets from their latest known location.")]
        public bool LatestLocation { get; set; }

        [Option("sas-suffixes", Separator = ',', HelpText = "List of potential uri suffixes that can be used if anonymous " +
            "access to a blob uri fails. Appended directly to the end of the URI (use full SAS suffix starting with '?'.")]
        public IEnumerable<string> SASSuffixes { get; set; }

        [Option("asset-filter", HelpText = "Only download assets matching the given regex filter")]
        public string AssetFilter { get; set; }

        public override Operation GetOperation()
        {
            return new GatherDropOperation(this);
        }
    }
}

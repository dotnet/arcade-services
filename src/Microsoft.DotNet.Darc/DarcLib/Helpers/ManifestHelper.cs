// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.Maestro.Client.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.DotNet.DarcLib.Helpers;

public class ManifestHelper
{
    public static JObject GenerateDarcAssetJsonManifest(IEnumerable<DownloadedBuild> downloadedBuilds, DropSizeReport dropSizeReport, string outputPath, bool makeAssetsRelativePaths)
    {
        return GenerateDarcAssetJsonManifest(downloadedBuilds, null, outputPath, makeAssetsRelativePaths);
    }


    public static JObject GenerateDarcAssetJsonManifest(IEnumerable<DownloadedBuild> downloadedBuilds, List<DownloadedAsset> alwaysDownloadedAssets, DropSizeReport dropSizeReport, string outputPath, bool makeAssetsRelativePaths)
    {

        // Construct an ad-hoc object with the necessary fields and use the json
        // serializer to write it to disk
        // If this type ever changes, we should consider giving it a specific versioned model object 

        // Null out the alwaysDownloadedAssets collection in the case where it's empty, so as to avoid serializing it.
        if (alwaysDownloadedAssets?.Count == 0)
        {
            alwaysDownloadedAssets = null;
        }

        var manifestObject = new
        {
            outputPath = outputPath,
            downloadSize = dropSizeReport?.DownloadSize,
            sizeOnDisk = dropSizeReport?.SizeOnDisk,
            sizeOfDuplicatedFilesBeforeUnpack = dropSizeReport?.SizeOfDuplicatedFilesBeforeUnpack,
            sizeOfDuplicatedFilesAfterUnpack = dropSizeReport?.SizeOfDuplicatedFilesAfterUnpack,
            builds = downloadedBuilds.Select(build =>
                new
                {
                    repo = build.Build.GitHubRepository ?? build.Build.AzureDevOpsRepository,
                    commit = build.Build.Commit,
                    branch = build.Build.AzureDevOpsBranch,
                    produced = build.Build.DateProduced,
                    buildNumber = build.Build.AzureDevOpsBuildNumber,
                    barBuildId = build.Build.Id,
                    channels = build.Build.Channels.Select(channel =>
                        new
                        {
                            id = channel.Id,
                            name = channel.Name
                        }),
                    assets = build.DownloadedAssets.Select(asset =>
                        new
                        {
                            name = asset.Asset.Name,
                            version = asset.Asset.Version,
                            nonShipping = asset.Asset.NonShipping,
                            source = asset.SourceLocation,
                            targets = GetTargetPaths(asset),
                            barAssetId = asset.Asset.Id,
                            size = asset.SizeInBytes
                        }),
                    dependencies = build.Dependencies?.Select(dependency =>
                        new
                        {
                            name = dependency.Name,
                            commit = dependency.Commit,
                            version = dependency.Version,
                            repoUri = dependency.RepoUri
                        })
                }),
            extraAssets = alwaysDownloadedAssets?.Select(extraAsset =>
                new
                {
                    name = extraAsset.Asset.Name,
                    version = extraAsset.Asset.Version,
                    nonShipping = extraAsset.Asset.NonShipping,
                    source = extraAsset.SourceLocation,
                    targets = GetTargetPaths(extraAsset),
                    barAssetId = extraAsset.Asset.Id,
                    size = extraAsset.SizeInBytes
                }),
            topLevelDuplicates = dropSizeReport?.DuplicatedAssets.Where(d => d.Locations.Any(l => l.optionalSubAssetPath == null))
                                                        .OrderByDescending(d => d.TotalSize)
                                                        .ThenBy(d => d.TotalCopies).Select(d =>
                new
                {
                    totalSize = d.TotalSize,
                    totalCopies = d.TotalCopies,
                    originalSize = d.OriginalSize,
                    locations = d.Locations.Select(l =>
                        new
                        {
                            path = l.location,
                            subpathInContainer = l.optionalSubAssetPath
                        })
                }),
            duplicates = dropSizeReport?.DuplicatedAssets.OrderByDescending(d => d.TotalSize)
                                                        .ThenBy(d => d.TotalCopies).Select(d =>
                new
                {
                    totalSize = d.TotalSize,
                    totalCopies = d.TotalCopies,
                    originalSize = d.OriginalSize,
                    locations = d.Locations.Select(l =>
                        new
                        {
                            path = l.location,
                            subpathInContainer = l.optionalSubAssetPath
                        })
                })
        };

        // If assetsRelativePath is provided, calculate the target path list as relative to the overall output directory
        List<string> GetTargetPaths(DownloadedAsset asset)
        {
            if (makeAssetsRelativePaths)
            {
                var list = new List<string>()
                {
                    Path.GetRelativePath(outputPath, asset.UnifiedLayoutTargetLocation)
                };

                if (asset.SeparatedLayoutTargetLocation != null)
                {
                    list.Add(Path.GetRelativePath(outputPath, asset.SeparatedLayoutTargetLocation));
                }

                if (asset.ReleasePackageLayoutTargetLocation != null)
                {
                    list.Add(Path.GetRelativePath(outputPath, asset.ReleasePackageLayoutTargetLocation));
                }
                return list;
            }
            else
            {
                var list = new List<string>()
                {
                    asset.UnifiedLayoutTargetLocation
                };

                if (asset.SeparatedLayoutTargetLocation != null)
                {
                    list.Add(asset.SeparatedLayoutTargetLocation);
                }

                if (asset.ReleasePackageLayoutTargetLocation != null)
                {
                    list.Add(asset.ReleasePackageLayoutTargetLocation);
                }
                return list;
            }
        }
        return JObject.FromObject(manifestObject, JsonSerializer.CreateDefault(new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore }));
    }
}

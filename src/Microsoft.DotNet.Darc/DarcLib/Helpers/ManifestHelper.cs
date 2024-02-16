// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Microsoft.DotNet.DarcLib.Helpers;

#nullable enable

public class ManifestHelper
{
    private const string MergedManifestFileName = "MergedManifest.xml";

    public static JObject GenerateDarcAssetJsonManifest(IEnumerable<DownloadedBuild> downloadedBuilds,
        string outputPath, bool makeAssetsRelativePaths, ILogger logger)
    {
        return GenerateDarcAssetJsonManifest(downloadedBuilds, null, outputPath, makeAssetsRelativePaths, logger);
    }


    public static JObject GenerateDarcAssetJsonManifest(IEnumerable<DownloadedBuild> downloadedBuilds, List<DownloadedAsset>? alwaysDownloadedAssets,
        string outputPath, bool makeAssetsRelativePaths, ILogger logger)
    {

        // Construct an ad-hoc object with the necessary fields and use the json
        // serializer to write it to disk
        // If this type ever changes, we should consider giving it a specific versioned model object 

        // Null out the alwaysDownloadedAssets collection in the case where it's empty, so as to avoid serializing it.
        if (alwaysDownloadedAssets?.Count == 0)
        {
            alwaysDownloadedAssets = null;
        }

        List<DownloadedAsset> mergedManifestAssets = SelectMergedManifestAssets(downloadedBuilds);
        Dictionary<string, string> assetCreatorMap = RetrieveAssetCreatorMap(mergedManifestAssets, logger);

        var manifestObject = new
        {
            outputPath = outputPath,
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
                            creator = assetCreatorMap.ContainsKey(asset.Asset.Name) ? assetCreatorMap[asset.Asset.Name] : null,
                            version = asset.Asset.Version,
                            nonShipping = asset.Asset.NonShipping,
                            source = asset.SourceLocation,
                            targets = GetTargetPaths(asset),
                            barAssetId = asset.Asset.Id
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
                    barAssetId = extraAsset.Asset.Id
                })
        };

        // If assetsRelativePath is provided, calculate the target path list as relative to the overall output directory
        List<string> GetTargetPaths(DownloadedAsset asset)
        {
            if (makeAssetsRelativePaths)
            {
                return
                [
                    Path.GetRelativePath(outputPath, asset.ReleaseLayoutTargetLocation),
                    Path.GetRelativePath(outputPath, asset.UnifiedLayoutTargetLocation)
                ];
            }
            else
            {
                return
                [
                    asset.ReleaseLayoutTargetLocation,
                    asset.UnifiedLayoutTargetLocation
                ];
            }
        }
        return JObject.FromObject(manifestObject, JsonSerializer.CreateDefault(new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore }));
    }

    private static List<DownloadedAsset> SelectMergedManifestAssets(IEnumerable<DownloadedBuild> downloadedBuilds)
    {
        return downloadedBuilds
            .Select(build => build.DownloadedAssets
                .Where(asset => asset.Asset.Name.EndsWith(MergedManifestFileName)))
            .SelectMany(x => x)
            .ToList();
    }

    private static Dictionary<string, string> RetrieveAssetCreatorMap(IEnumerable<DownloadedAsset> mergedManifests, ILogger logger)
    {
        Dictionary<string, string> assetCreatorMap = new();

        foreach (var mergedManifest in mergedManifests)
        {
            XDocument? document = null;
            try
            {
                document = XDocument.Load(mergedManifest.UnifiedLayoutTargetLocation);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Warning: Loading '{mergedManifest.UnifiedLayoutTargetLocation}' failed with exception: {ex}.");
                continue;
            }

            XElement? buildElement = document.Element("Build");

            string? buildName = buildElement?.Attribute("Name")?.Value;
            if (buildName == null)
                continue;

            string repoName = buildName.Replace("dotnet-", string.Empty);

            foreach (var asset in buildElement?.Elements() ?? [])
            {
                string? assetName = asset.Attribute("Id")?.Value;
                if (assetName == null)
                    continue;

                string? assetCreator = asset.Attribute("Creator")?.Value;

                // An Creator attribute has been added to the MergeManifest.xml file for the VMR build to differentiate assets produced by different repos,
                // as mentioned in https://github.com/dotnet/source-build/issues/3898.
                // To standardize the generation of the manifest.json file for .NET 6/7/8 and VMR builds,
                // the Creator attribute is taken from the MergeManifest.xml file if it exists, otherwise,
                // it is taken from the Build.Name, which corresponds to the repository name.
                assetCreator ??= repoName;

                if (assetCreatorMap.TryGetValue(assetName, out var existingAssetCreator))
                {
                    if (existingAssetCreator != assetCreator)
                    {
                        logger.LogWarning($"Warning: The same asset '{assetName}' is listed in various '{MergedManifestFileName}', " +
                            $"with differing creators specified in each: {existingAssetCreator}, {assetCreator}.");
                    }
                }
                else
                {
                    assetCreatorMap.Add(assetName, assetCreator);
                }
            }
        }

        return assetCreatorMap;
    }
}

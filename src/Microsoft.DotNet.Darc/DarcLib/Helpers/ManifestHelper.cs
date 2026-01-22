// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Microsoft.DotNet.DarcLib.Helpers;

#nullable enable

public class ManifestHelper
{
    private const string MergedManifestFileName = "MergedManifest.xml";

    public static JObject GenerateDarcAssetJsonManifest(
        IEnumerable<DownloadedBuild> downloadedBuilds,
        string outputPath,
        bool makeAssetsRelativePaths,
        ILogger logger)
    {
        return GenerateDarcAssetJsonManifest(downloadedBuilds, null, outputPath, makeAssetsRelativePaths, logger);
    }

    public static JObject GenerateDarcAssetJsonManifest(
        IEnumerable<DownloadedBuild> downloadedBuilds,
        List<DownloadedAsset>? alwaysDownloadedAssets,
        string outputPath,
        bool makeAssetsRelativePaths,
        ILogger logger)
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
        Dictionary<string, AssetReleaseMetadata> assetReleaseMetadataMap = RetrieveAssetReleaseMetadata(mergedManifestAssets, logger);

        var manifestObject = new
        {
            outputPath = outputPath,
            builds = downloadedBuilds.Select(build =>
                new
                {
                    repo = build.Build.GetRepository(),
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
                            origin = assetReleaseMetadataMap.TryGetValue(asset.Asset.Name, out var data) ? data.Origin : null,
                            dotnetReleaseShipping = data != null && data.DotNetReleaseShipping,
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
            .SelectMany(build => build.DownloadedAssets
                .Where(asset => asset.Asset.Name.EndsWith(MergedManifestFileName)))
            .ToList();
    }

    private static Dictionary<string, AssetReleaseMetadata> RetrieveAssetReleaseMetadata(IEnumerable<DownloadedAsset> mergedManifests, ILogger logger)
    {
        Dictionary<string, AssetReleaseMetadata> assetMetadataMap = [];

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

            string repoName = buildName.Replace("dotnet-", null);

            foreach (var asset in buildElement?.Elements() ?? [])
            {
                string? assetName = asset.Attribute("Id")?.Value;
                if (assetName == null)
                    continue;

                string? assetOrigin = asset.Attribute("Origin")?.Value;
                bool dotNetReleaseShipping = asset.Attribute("DotNetReleaseShipping")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

                // An Origin attribute has been added to the MergeManifest.xml file for the VMR build to differentiate assets produced by different repos,
                // as mentioned in https://github.com/dotnet/source-build/issues/3898.
                // To standardize the generation of the manifest.json file for .NET 6/7/8 and VMR builds,
                // the Origin attribute is taken from the MergeManifest.xml file if it exists, otherwise,
                // it is taken from the Build.Name, which corresponds to the repository name.
                assetOrigin ??= repoName;

                if (assetMetadataMap.TryGetValue(assetName, out var existingAssetMetadata))
                {
                    if (existingAssetMetadata.Origin != assetOrigin)
                    {
                        logger.LogWarning($"Warning: The same asset '{assetName}' is listed in various '{MergedManifestFileName}', " +
                            $"with differing origins specified in each: {existingAssetMetadata.Origin}, {assetOrigin}.");
                    }
                }
                else
                {
                    assetMetadataMap.Add(assetName, new AssetReleaseMetadata(assetOrigin, dotNetReleaseShipping));
                }
            }
        }

        return assetMetadataMap;
    }

    private record AssetReleaseMetadata(string Origin, bool DotNetReleaseShipping);

    /// <summary>
    /// Downloads and parses the MergedManifest.xml from a build to retrieve repo origins for assets.
    /// </summary>
    /// <param name="build">The build to get the MergedManifest.xml from</param>
    /// <param name="httpClient">HTTP client for downloading the manifest</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <returns>A dictionary mapping asset names to their repo origins, or null if MergedManifest.xml is not found</returns>
    public static async Task<Dictionary<string, string>?> GetAssetRepoOriginsAsync(
        Build build,
        HttpClient httpClient,
        ILogger logger)
    {
        // Find the MergedManifest.xml asset
        var mergedManifestAsset = build.Assets?
            .FirstOrDefault(a => a.Name.EndsWith(MergedManifestFileName, StringComparison.OrdinalIgnoreCase));

        if (mergedManifestAsset == null)
        {
            logger.LogInformation("Build {BuildId} does not contain a {FileName} asset.", build.Id, MergedManifestFileName);
            return null;
        }

        // Get the asset location
        var assetLocation = mergedManifestAsset.Locations?.FirstOrDefault();
        if (assetLocation == null || string.IsNullOrEmpty(assetLocation.Location))
        {
            logger.LogWarning($"MergedManifest.xml asset found but has no location information.");
            return null;
        }

        // Download the manifest
        string manifestContent;
        try
        {
            var manifestUrl = GetManifestDownloadUrl(assetLocation.Location, mergedManifestAsset.Name);
            logger.LogInformation("Downloading MergedManifest.xml from {ManifestUrl}", manifestUrl);

            var response = await httpClient.GetAsync(manifestUrl);
            response.EnsureSuccessStatusCode();
            manifestContent = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to download MergedManifest.xml from build {BuildId}", build.Id);
            return null;
        }

        // Parse the manifest to extract repo origins
        return ParseAssetRepoOrigins(manifestContent, logger);
    }

    /// <summary>
    /// Constructs the download URL for the MergedManifest.xml asset based on its location.
    /// </summary>
    private static string GetManifestDownloadUrl(string assetLocation, string assetName)
    {
        // If the location is a blob storage URL ending in index.json, strip that and append the asset name
        if (assetLocation.EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = assetLocation.Substring(0, assetLocation.LastIndexOf("index.json"));
            return $"{baseUrl}{assetName}";
        }

        // Otherwise assume the location is directly usable
        return assetLocation;
    }

    /// <summary>
    /// Parses the MergedManifest.xml content to extract repo origins for each asset.
    /// </summary>
    private static Dictionary<string, string> ParseAssetRepoOrigins(string manifestContent, ILogger logger)
    {
        var assetOrigins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var document = XDocument.Parse(manifestContent);
            var buildElement = document.Element("Build");

            if (buildElement == null)
            {
                logger.LogWarning("MergedManifest.xml does not contain a Build element.");
                return assetOrigins;
            }

            string? buildName = buildElement.Attribute("Name")?.Value;
            string repoName = buildName?.Replace("dotnet-", null) ?? string.Empty;

            foreach (var asset in buildElement.Elements())
            {
                string? assetName = asset.Attribute("Id")?.Value;
                if (assetName == null)
                    continue;

                string? assetOrigin = asset.Attribute("Origin")?.Value;

                // If Origin attribute doesn't exist, use the build name (repo name) as the origin
                assetOrigin ??= repoName;

                if (!string.IsNullOrEmpty(assetOrigin) && !assetOrigins.ContainsKey(assetName))
                {
                    assetOrigins.Add(assetName, assetOrigin);
                }
            }

            logger.LogInformation("Parsed {Count} asset origin mappings from MergedManifest.xml", assetOrigins.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse MergedManifest.xml");
        }

        return assetOrigins;
    }
}

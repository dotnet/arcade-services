// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Models.Darc;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Microsoft.DotNet.DarcLib.Helpers
{
    public class ManifestHelper
    {
        public static JObject GenerateDarcAssetJsonManifest(IEnumerable<DownloadedBuild> downloadedBuilds, string outputPath, bool makeAssetsRelativePaths)
        {
            // Construct an ad-hoc object with the necessary fields and use the json
            // serializer to write it to disk
            // If this type ever changes, we should consider giving it a specific versioned model object 
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
                            version = asset.Asset.Version,
                            nonShipping = asset.Asset.NonShipping,
                            source = asset.SourceLocation,
                            targets = GetTargetPaths(asset),
                            barAssetId = asset.Asset.Id
                        })
                    })
            };

            // If assetsRelativePath is provided, calculate the target path list as relative to the overall output directory
            List<string> GetTargetPaths(DownloadedAsset asset)
            {
                if (makeAssetsRelativePaths)
                {
                    return new List<string>
                    {
                        Path.GetRelativePath(outputPath, asset.ReleaseLayoutTargetLocation),
                        Path.GetRelativePath(outputPath, asset.UnifiedLayoutTargetLocation)
                    };
                }
                else
                {
                    return new List<string>
                    {
                        asset.ReleaseLayoutTargetLocation,
                        asset.UnifiedLayoutTargetLocation
                    };
                }
            }

            return JObject.FromObject(manifestObject);
        }
    }
}

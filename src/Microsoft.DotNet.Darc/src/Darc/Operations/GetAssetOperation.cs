// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    /// <summary>
    ///     Retrieve information about a specific asset by name.
    /// </summary>
    internal class GetAssetOperation : Operation
    {
        private GetAssetCommandLineOptions _options;

        public GetAssetOperation(GetAssetCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

            try
            {
                Channel targetChannel = null;
                if (!string.IsNullOrEmpty(_options.Channel))
                {
                    targetChannel = await UxHelpers.ResolveSingleChannel(remote, _options.Channel);
                    if (targetChannel == null)
                    {
                        return Constants.ErrorCode;
                    }
                }

                // Starting with the remote, get information on the asset name + version
                List<Asset> matchingAssets =
                    (await remote.GetAssetsAsync(name: _options.Name, version: _options.Version)).ToList();

                string queryDescriptionString =
                    $"name '{_options.Name}'{(!string.IsNullOrEmpty(_options.Version) ? $"and version '{_options.Version}'" : "")}" +
                    $"{(targetChannel != null ? $" on channel '{targetChannel.Name}'" : "")} in the last {_options.MaxAgeInDays} days";

                Console.WriteLine($"Looking up assets with {queryDescriptionString}");

                // Walk the assets and look up the corresponding builds, potentially filtering based on channel
                // if there is a target channel
                bool foundMatching = false;
                int maxAgeInDays = _options.MaxAgeInDays;
                var now = DateTimeOffset.Now;
                int checkedAssets = 0;

                foreach (var asset in matchingAssets)
                {
                    checkedAssets++;

                    // Get build info for asset
                    Build buildInfo = await remote.GetBuildAsync(asset.BuildId);

                    if (targetChannel != null && !buildInfo.Channels.Any(c => c.Id == targetChannel.Id))
                    {
                        continue;
                    }

                    if (now.Subtract(buildInfo.DateProduced).TotalDays > maxAgeInDays)
                    {
                        break;
                    }

                    foundMatching = true;

                    Console.WriteLine($"{asset.Name} @ {asset.Version}");
                    Console.WriteLine(UxHelpers.GetBuildDescription(buildInfo));
                }

                if (!foundMatching)
                {
                    Console.WriteLine($"No assets found with {queryDescriptionString}");
                    int remaining = matchingAssets.Count - checkedAssets;
                    if (remaining > 0)
                    {
                        Console.WriteLine($"Skipping build lookup for {remaining} assets. Consider increasing --max-age to check the rest.");
                    }
                }

                return Constants.SuccessCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error: Failed to retrieve information about assets.");
                return Constants.ErrorCode;
            }
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class GetLatestBuildOperation : Operation
    {
        GetLatestBuildCommandLineOptions _options;
        public GetLatestBuildOperation(GetLatestBuildCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        ///     Gets the latest build for a repo
        /// </summary>
        /// <returns>Process exit code.</returns>
        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

                // Calculate out possible repos based on the input strings.
                // Today the DB has no way of searching for builds by substring, so for now
                // grab source/targets repos of subscriptions matched on substring,
                // and then add the explicit repo from the options.
                // Then search channels by substring
                // Then run GetLatestBuild for each permutation.

                var subscriptions = await remote.GetSubscriptionsAsync();
                var possibleRepos = subscriptions
                    .SelectMany(subscription => new List<string> { subscription.SourceRepository, subscription.TargetRepository })
                    .Where(r => r.Contains(_options.Repo, StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                possibleRepos.Add(_options.Repo);

                var channels = (await remote.GetChannelsAsync())
                    .Where(c => string.IsNullOrEmpty(_options.Channel) || c.Name.Contains(_options.Channel, StringComparison.OrdinalIgnoreCase));

                if (!channels.Any())
                {
                    Console.WriteLine($"Could not find a channel with name containing '{_options.Channel}'");
                    return Constants.ErrorCode;
                }

                bool foundBuilds = false;
                foreach (string possibleRepo in possibleRepos)
                {
                    foreach (Channel channel in channels)
                    {
                        Build latestBuild = await remote.GetLatestBuildAsync(possibleRepo, channel.Id);
                        if (latestBuild != null)
                        {
                            if (foundBuilds)
                            {
                                Console.WriteLine();
                            }
                            foundBuilds = true;
                            Console.Write(UxHelpers.GetTextBuildDescription(latestBuild));
                        }
                    }
                }

                if (!foundBuilds)
                {
                    Console.WriteLine("No latest build found matching the specified criteria");
                    return Constants.ErrorCode;
                }

                return Constants.SuccessCode;
            }
            catch (AuthenticationException e)
            {
                Console.WriteLine(e.Message);
                return Constants.ErrorCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error: Failed to retrieve latest build.");
                return Constants.ErrorCode;
            }
        }
    }
}

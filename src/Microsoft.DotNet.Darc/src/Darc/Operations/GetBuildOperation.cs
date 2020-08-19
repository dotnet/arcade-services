// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class GetBuildOperation : Operation
    {
        GetBuildCommandLineOptions _options;
        public GetBuildOperation(GetBuildCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        ///     Get a specific build of a repository
        /// </summary>
        /// <returns>Process exit code.</returns>
        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

                List<Build> matchingBuilds = null;
                if (_options.Id != 0)
                {
                    if (!string.IsNullOrEmpty(_options.Repo) ||
                        !string.IsNullOrEmpty(_options.Commit))
                    {
                        Console.WriteLine("--id should not be used with other options.");
                        return Constants.ErrorCode;
                    }

                    matchingBuilds = new List<Build>() { await remote.GetBuildAsync(_options.Id) };
                }
                else if (!string.IsNullOrEmpty(_options.Repo) || !string.IsNullOrEmpty(_options.Commit))
                {
                    if (string.IsNullOrEmpty(_options.Repo) != string.IsNullOrEmpty(_options.Commit))
                    {
                        Console.WriteLine("--repo and --commit should be used together.");
                        return Constants.ErrorCode;
                    }
                    var subscriptions = await remote.GetSubscriptionsAsync();
                    var possibleRepos = subscriptions
                        .SelectMany(subscription => new List<string> { subscription.SourceRepository, subscription.TargetRepository })
                        .Where(r => r.Contains(_options.Repo, StringComparison.OrdinalIgnoreCase))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    matchingBuilds = new List<Build>();
                    foreach (string repo in possibleRepos)
                    {
                        matchingBuilds.AddRange(await remote.GetBuildsAsync(repo, _options.Commit));
                    }
                }
                else
                {
                    Console.WriteLine("Please specify --id, --uri, or --repo and --commit to lookup a build.");
                    return Constants.ErrorCode;
                }

                // Print the build info.
                if (!matchingBuilds.Any())
                {
                    Console.WriteLine($"Could not any builds matching the given criteria.");
                    return Constants.ErrorCode;
                }

                switch (_options.OutputFormat)
                {
                    case DarcOutputType.text:
                        foreach (Build build in matchingBuilds)
                        {
                            Console.Write(UxHelpers.GetTextBuildDescription(build));
                        }
                        break;
                    case DarcOutputType.json:
                        Console.WriteLine(JsonConvert.SerializeObject(
                            matchingBuilds.Select(build => UxHelpers.GetJsonBuildDescription(build)), Formatting.Indented));
                        break;
                    default:
                        throw new NotImplementedException($"Output format type {_options.OutputFormat} not yet supported for get-build.");
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
                Logger.LogError(e, "Error: Failed to retrieve build information.");
                return Constants.ErrorCode;
            }
        }
    }
}

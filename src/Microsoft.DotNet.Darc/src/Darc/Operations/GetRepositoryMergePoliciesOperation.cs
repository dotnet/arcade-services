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
    internal class GetRepositoryMergePoliciesOperation : Operation
    {
        private GetRepositoryMergePoliciesCommandLineOptions _options;

        public GetRepositoryMergePoliciesOperation(GetRepositoryMergePoliciesCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

                IEnumerable<RepositoryBranch> allRepositories = await remote.GetRepositoriesAsync();
                IEnumerable<RepositoryBranch> filteredRepositories = allRepositories.Where(repositories =>
                    (string.IsNullOrEmpty(_options.Repo) || repositories.Repository.Contains(_options.Repo, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrEmpty(_options.Branch) || repositories.Branch.Contains(_options.Branch, StringComparison.OrdinalIgnoreCase)));

                // List only those repos and branches that are targeted by a batchable subscription (active) unless the user
                // passes --all.
                if (!_options.All)
                {
                    HashSet<string> batchableTargets = (await remote.GetSubscriptionsAsync())
                        .Where(s => s.Policy.Batchable)
                        .Select<Subscription, string>(s => $"{s.TargetRepository}{s.TargetBranch}")
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var targetedRepositories = filteredRepositories.Where(r => batchableTargets.Contains($"{r.Repository}{r.Branch}"));

                    // If the number of repositories we're about to print is less than what we could have printed, then print a
                    // message.
                    int difference = filteredRepositories.Count() - targetedRepositories.Count();
                    if (difference != 0)
                    {
                        Console.WriteLine($"Filtered {difference} policies for branches not targeted by an active batchable subscription. To include, pass --all.{Environment.NewLine}");
                    }

                    filteredRepositories = targetedRepositories;
                }

                foreach (var repository in filteredRepositories)
                {
                    Console.WriteLine($"{repository.Repository} @ {repository.Branch}");
                    Console.Write(UxHelpers.GetMergePoliciesDescription(repository.MergePolicies));
                }

                return Constants.SuccessCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error: Failed to retrieve repositories");
                return Constants.ErrorCode;
            }
        }
    }
}

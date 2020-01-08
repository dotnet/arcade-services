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
    /// Retrieves a list of subscriptions based on input information
    /// </summary>
    class GetSubscriptionsOperation : Operation
    {
        GetSubscriptionsCommandLineOptions _options;
        public GetSubscriptionsOperation(GetSubscriptionsCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

                IEnumerable<Subscription> subscriptions = await _options.FilterSubscriptions(remote);

                if (!subscriptions.Any())
                {
                    Console.WriteLine("No subscriptions found matching the specified criteria.");
                    return Constants.ErrorCode;
                }

                // Based on the current output schema, sort by source repo, target repo, target branch, etc.
                // Concat the input strings as a simple sorting mechanism.
                foreach (var subscription in subscriptions.OrderBy(subscription =>
                                            $"{subscription.SourceRepository}{subscription.Channel}{subscription.TargetRepository}{subscription.TargetBranch}"))
                {
                    Console.WriteLine($"{subscription.SourceRepository} ({subscription.Channel.Name}) ==> '{subscription.TargetRepository}' ('{subscription.TargetBranch}')");
                    Console.WriteLine($"  - Id: {subscription.Id}");
                    Console.WriteLine($"  - Update Frequency: {subscription.Policy.UpdateFrequency}");
                    Console.WriteLine($"  - Enabled: {subscription.Enabled}");
                    Console.WriteLine($"  - Batchable: {subscription.Policy.Batchable}");
                    // If batchable, the merge policies come from the repository
                    IEnumerable<MergePolicy> mergePolicies = subscription.Policy.MergePolicies;
                    if (subscription.Policy.Batchable == true)
                    {
                        mergePolicies = await remote.GetRepositoryMergePoliciesAsync(subscription.TargetRepository, subscription.TargetBranch);
                    }

                    Console.Write(UxHelpers.GetMergePoliciesDescription(mergePolicies, "  "));

                    // Currently the API only returns the last applied build for requests to specific subscriptions.
                    // This will be fixed, but for now, don't print the last applied build otherwise.
                    if (subscription.LastAppliedBuild != null)
                    {
                        Console.WriteLine($"  - Last Build: {subscription.LastAppliedBuild.AzureDevOpsBuildNumber} ({subscription.LastAppliedBuild.Commit})");
                    }
                }
                return Constants.SuccessCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error: Failed to retrieve subscriptions");
                return Constants.ErrorCode;
            }
        }
    }
}

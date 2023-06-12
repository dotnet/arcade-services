// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Darc.Operations;

/// <summary>
/// Retrieves a list of subscriptions based on input information
/// </summary>
class GetSubscriptionsOperation : Operation
{
    private readonly GetSubscriptionsCommandLineOptions _options;

    public GetSubscriptionsOperation(GetSubscriptionsCommandLineOptions options, IServiceCollection? services = null)
        : base(options, services)
    {
        _options = options;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            IRemote remote = Provider.GetService<IRemote>() ??  RemoteFactory.GetBarOnlyRemote(_options, Logger);

            IEnumerable<Subscription> subscriptions = await _options.FilterSubscriptions(remote);

            if (!subscriptions.Any())
            {
                Console.WriteLine("No subscriptions found matching the specified criteria.");
                return Constants.ErrorCode;
            }

            switch (_options.OutputFormat)
            {
                case DarcOutputType.json:
                    await OutputJsonAsync(subscriptions, remote);
                    break;
                case DarcOutputType.text:
                    await OutputTextAsync(subscriptions, remote);
                    break;
                default:
                    throw new NotImplementedException($"Output type {_options.OutputFormat} not supported by get-subscriptions");
            }

            return Constants.SuccessCode;
        }
        catch (AuthenticationException ex)
        {
            Console.WriteLine(ex.Message);
            return Constants.ErrorCode;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error: Failed to retrieve subscriptions");
            return Constants.ErrorCode;
        }
    }

    protected override bool IsOutputFormatSupported(DarcOutputType outputFormat)
        => outputFormat switch
        {
            DarcOutputType.json => true,
            _ => base.IsOutputFormatSupported(outputFormat),
        };

    private static async Task OutputJsonAsync(IEnumerable<Subscription> subscriptions, IRemote remote)
    {
        foreach (var subscription in Sort(subscriptions))
        {
            // If batchable, the merge policies come from the repository
            if (subscription.Policy.Batchable)
            {
                IEnumerable<MergePolicy> repoMergePolicies = await remote.GetRepositoryMergePoliciesAsync(subscription.TargetRepository, subscription.TargetBranch);
                if (!repoMergePolicies.Any())
                {
                    continue;
                }

                IEnumerable<MergePolicy> mergePolicies = subscription.Policy.MergePolicies;
                subscription.Policy.MergePolicies = mergePolicies.Union(repoMergePolicies).ToImmutableList();
            }
        }

        Console.WriteLine(JsonConvert.SerializeObject(subscriptions, Formatting.Indented));
    }

    private static async Task OutputTextAsync(IEnumerable<Subscription> subscriptions, IRemote remote)
    {
        foreach (var subscription in Sort(subscriptions))
        {
            // If batchable, the merge policies come from the repository
            IEnumerable<MergePolicy> mergePolicies = subscription.Policy.MergePolicies;
            if (subscription.Policy.Batchable)
            {
                mergePolicies = await remote.GetRepositoryMergePoliciesAsync(subscription.TargetRepository, subscription.TargetBranch);
            }

            string subscriptionInfo = UxHelpers.GetTextSubscriptionDescription(subscription, mergePolicies);
            Console.Write(subscriptionInfo);
        }
    }

    // Based on the current output schema, sort by source repo, target repo, target branch, etc.
    // Concat the input strings as a simple sorting mechanism.
    private static IEnumerable<Subscription> Sort(IEnumerable<Subscription> subscriptions)
        => subscriptions.OrderBy(subscription => $"{subscription.SourceRepository}{subscription.Channel}{subscription.TargetRepository}{subscription.TargetBranch}");
}

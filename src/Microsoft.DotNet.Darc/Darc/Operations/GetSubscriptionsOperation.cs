// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

/// <summary>
/// Retrieves a list of subscriptions based on input information
/// </summary>
internal class GetSubscriptionsOperation : Operation
{
    private readonly GetSubscriptionsCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<GetSubscriptionsOperation> _logger;

    public GetSubscriptionsOperation(
        GetSubscriptionsCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<GetSubscriptionsOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            IEnumerable<Subscription> subscriptions = await _options.FilterSubscriptions(_barClient);

            if (!subscriptions.Any())
            {
                Console.WriteLine("No subscriptions found matching the specified criteria.");
                return Constants.ErrorCode;
            }

            switch (_options.OutputFormat)
            {
                case DarcOutputType.json:
                    await OutputJsonAsync(subscriptions, _barClient);
                    break;
                case DarcOutputType.text:
                    await OutputTextAsync(subscriptions, _barClient);
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
            _logger.LogError(ex, "Error: Failed to retrieve subscriptions");
            return Constants.ErrorCode;
        }
    }

    private static async Task OutputJsonAsync(IEnumerable<Subscription> subscriptions, IBarApiClient barClient)
    {
        foreach (var subscription in Sort(subscriptions))
        {
            // If batchable, the merge policies come from the repository
            if (subscription.Policy.Batchable)
            {
                IEnumerable<MergePolicy> repoMergePolicies = await barClient.GetRepositoryMergePoliciesAsync(subscription.TargetRepository, subscription.TargetBranch);
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

    private static async Task OutputTextAsync(IEnumerable<Subscription> subscriptions, IBarApiClient barClient)
    {
        foreach (var subscription in Sort(subscriptions))
        {
            // If batchable, the merge policies come from the repository
            IEnumerable<MergePolicy> mergePolicies = subscription.Policy.MergePolicies;
            if (subscription.Policy.Batchable)
            {
                mergePolicies = await barClient.GetRepositoryMergePoliciesAsync(subscription.TargetRepository, subscription.TargetBranch);
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

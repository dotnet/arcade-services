// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

/// <summary>
/// Retrieves a list of subscriptions based on input information
/// </summary>
internal class GetSubscriptionsOperation : Operation
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

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
            IReadOnlyList<Subscription> subscriptions = [.. await _options.FilterSubscriptions(_barClient)];

            if (subscriptions.Count == 0)
            {
                Console.WriteLine("No subscriptions found matching the specified criteria.");
                return Constants.ErrorCode;
            }

            subscriptions = await ApplyRepositoryMergeSettingsAsync(subscriptions, _barClient);

            switch (_options.OutputFormat)
            {
                case DarcOutputType.json:
                    OutputJson(subscriptions);
                    break;
                case DarcOutputType.text:
                    OutputText(subscriptions);
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

    private static async Task<IReadOnlyList<Subscription>> ApplyRepositoryMergeSettingsAsync(
        IEnumerable<Subscription> subscriptions,
        IBarApiClient barClient)
    {
        List<Subscription> subscriptionsWithMergeSettings = [];

        foreach (Subscription subscription in subscriptions)
        {
            if (!subscription.Policy.Batchable)
            {
                subscriptionsWithMergeSettings.Add(subscription);
                continue;
            }

            RepositoryBranch? repositoryBranch = (await barClient.GetRepositoriesAsync(
                subscription.TargetRepository,
                subscription.TargetBranch))
                .SingleOrDefault();

            if (repositoryBranch == null)
            {
                subscriptionsWithMergeSettings.Add(subscription);
                continue;
            }

            subscriptionsWithMergeSettings.Add(new Subscription(
                subscription.Id,
                repositoryBranch.MergePrs,
                subscription.Enabled,
                subscription.SourceEnabled,
                subscription.AutoApprove,
                subscription.SourceRepository,
                subscription.TargetRepository,
                subscription.TargetBranch,
                [.. repositoryBranch.IgnoredChecks ?? []],
                subscription.SourceDirectory,
                subscription.TargetDirectory,
                subscription.PullRequestFailureNotificationTags,
                subscription.ExcludedAssets)
            {
                Channel = subscription.Channel,
                Policy = subscription.Policy,
                LastAppliedBuild = subscription.LastAppliedBuild,
            });
        }

        return subscriptionsWithMergeSettings;
    }

    private static void OutputJson(IEnumerable<Subscription> subscriptions)
        => Console.WriteLine(JsonSerializer.Serialize(subscriptions, JsonOptions));

    private static void OutputText(IEnumerable<Subscription> subscriptions)
    {
        foreach (var subscription in Sort(subscriptions))
        {
            string subscriptionInfo = UxHelpers.GetTextSubscriptionDescription(subscription);
            Console.Write(subscriptionInfo);
        }
    }

    // Based on the current output schema, sort by source repo, target repo, target branch, etc.
    // Concat the input strings as a simple sorting mechanism.
    private static IEnumerable<Subscription> Sort(IEnumerable<Subscription> subscriptions)
        => subscriptions.OrderBy(subscription => $"{subscription.SourceRepository}{subscription.Channel}{subscription.TargetRepository}{subscription.TargetBranch}");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        DefaultJsonTypeInfoResolver typeInfoResolver = new();
        typeInfoResolver.Modifiers.Add(typeInfo =>
        {
            List<JsonPropertyInfo> ignoredProperties = [.. typeInfo.Properties.Where(property =>
                property.AttributeProvider is System.Reflection.MemberInfo memberInfo &&
                memberInfo.GetCustomAttributesData().Any(attribute =>
                    attribute.AttributeType.FullName == "Newtonsoft.Json.JsonIgnoreAttribute"))];
            foreach (JsonPropertyInfo ignoredProperty in ignoredProperties)
            {
                typeInfo.Properties.Remove(ignoredProperty);
            }
        });

        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = typeInfoResolver,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

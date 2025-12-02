// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using Microsoft.DotNet.DarcLib.Models.Yaml;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Newtonsoft.Json.Linq;

namespace ProductConstructionService.Cli.Operations;

internal class ExportConfigurationOperation : IOperation
{
    private readonly IProductConstructionServiceApi _api;

    public ExportConfigurationOperation(IProductConstructionServiceApi api) => _api = api;

    public async Task<int> RunAsync()
    {
        var subscriptions = await _api.Subscriptions.ListSubscriptionsAsync();
        var subscriptionYamls = subscriptions
            .Select(sub => new SubscriptionYaml
                {
                    Id = sub.Id.ToString(),
                    Enabled = sub.Enabled.ToString(),
                    Channel = sub.Channel?.Name ?? string.Empty,
                    SourceRepository = sub.SourceRepository,
                    TargetRepository = sub.TargetRepository,
                    TargetBranch = sub.TargetBranch,
                    UpdateFrequency = sub.Policy?.UpdateFrequency.ToString() ?? string.Empty,
                    Batchable = sub.Policy?.Batchable.ToString() ?? "False",
                    MergePolicies = ConvertMergePolicies(sub.Policy?.MergePolicies),
                    FailureNotificationTags = sub.PullRequestFailureNotificationTags ?? string.Empty,
                    SourceEnabled = sub.SourceEnabled.ToString(),
                    SourceDirectory = sub.SourceDirectory ?? string.Empty,
                    TargetDirectory = sub.TargetDirectory ?? string.Empty,
                    ExcludedAssets = sub.ExcludedAssets?.ToList() ?? [],
                })
            .Select(subYaml => (MaestroConfigHelper.GetDefaultSubscriptionFilePath(subYaml), subYaml))
            .GroupBy(t => t.Item1, t => t.subYaml);

        foreach (var group in subscriptionYamls)
        {
            var filePath = group.Key;
            var subsInFile = group.ToList();

        }

        return 0;
    }

    private static List<MergePolicyYaml> ConvertMergePolicies(IEnumerable<MergePolicy>? mergePolicies)
    {
        if (mergePolicies == null)
        {
            return [];
        }

        return mergePolicies.Select(policy => new MergePolicyYaml
        {
            Name = policy.Name,
            Properties = policy.Properties != null
                ? policy.Properties.ToDictionary(
                    p => p.Key,
                    p => p.Value.Type switch
                    {
                        JTokenType.Array => (object)p.Value.ToObject<List<object>>()!,
                        _ => throw new NotImplementedException($"Unexpected property value type {p.Value.Type}")
                    })
                : []
        }).ToList();
    }
}

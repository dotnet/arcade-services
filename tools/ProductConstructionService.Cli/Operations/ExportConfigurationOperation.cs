// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Yaml;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Newtonsoft.Json.Linq;
using ProductConstructionService.Cli.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ProductConstructionService.Cli.Operations;

internal class ExportConfigurationOperation : IOperation
{
    private readonly IProductConstructionServiceApi _api;
    private readonly ExportConfigurationOptions _options;
    private readonly IFileSystem _fileSystem;

    private static readonly ISerializer _yamlSerializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .Build();

    public ExportConfigurationOperation(IProductConstructionServiceApi api, ExportConfigurationOptions options, IFileSystem fileSystem)
    {
        _api = api;
        _options = options;
        _fileSystem = fileSystem;
    }

    public async Task<int> RunAsync()
    {
        NativePath exportPath = new(_options.ExportPath);

        await ExportSubscriptions(exportPath);
        await ExportChannels(exportPath);
        await ExportDefaultChannels(exportPath);
        await ExportBranchMergePolicies(exportPath);

        return 0;
    }

    private async Task ExportSubscriptions(NativePath exportPath)
    {
        var subscriptions = await _api.Subscriptions.ListSubscriptionsAsync();
        var subscriptionYamlGroups = subscriptions
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

        _fileSystem.CreateDirectory(exportPath / MaestroConfigHelper.SubscriptionFolderPath);
        WriteGroupsToFiles(exportPath, subscriptionYamlGroups);
    }

    private async Task ExportChannels(NativePath exportPath)
    {
        var channels = await _api.Channels.ListChannelsAsync();
        var channelYamlGroups = channels
            .Select(channel => new ChannelYaml
            {
                Name = channel.Name,
                Classification = channel.Classification,
            })
            .Select(channelYaml => (MaestroConfigHelper.GetDefaultChannelFilePath(channelYaml), channelYaml))
            .GroupBy(t => t.Item1, t => t.channelYaml);

        _fileSystem.CreateDirectory(exportPath / MaestroConfigHelper.ChannelFolderPath);
        WriteGroupsToFiles(exportPath, channelYamlGroups);
    }

    private async Task ExportDefaultChannels(NativePath exportPath)
    {
        var defaultChannels = await _api.DefaultChannels.ListAsync();
        var defaultChannelYamlGroups = defaultChannels
            .Select(dc => new DefaultChannelYaml
            {
                Repository = dc.Repository,
                Branch = dc.Branch,
                ChannelId = dc.Channel.Id,
                Enabled = dc.Enabled,
            })
            .Select(dcYaml => (MaestroConfigHelper.GetDefaultDefaultChannelFilePath(dcYaml), dcYaml))
            .GroupBy(t => t.Item1, t => t.dcYaml);
        _fileSystem.CreateDirectory(exportPath / MaestroConfigHelper.DefaultChannelFolderPath);
        WriteGroupsToFiles(exportPath, defaultChannelYamlGroups);
    }

    private async Task ExportBranchMergePolicies(NativePath exportPath)
    {
        var repoBranches = await _api.Repository.ListRepositoriesAsync();
        var branchMergePolicyGroups = repoBranches
            .Select(rb => new BranchMergePoliciesYaml
            {
                Repository = rb.Repository,
                Branch = rb.Branch,
                MergePolicies = ConvertMergePolicies(rb.MergePolicies),
            })
            .Select(bmpYaml => (MaestroConfigHelper.GetDefaultRepositoryBranchFilePath(bmpYaml), bmpYaml))
            .GroupBy(t => t.Item1, t => t.bmpYaml);
        _fileSystem.CreateDirectory(exportPath / MaestroConfigHelper.RepositoryBranchFolderPath);
        WriteGroupsToFiles(exportPath, branchMergePolicyGroups);
    }

    private void WriteGroupsToFiles(NativePath exportPath, IEnumerable<IGrouping<string, object>> groups)
    {
        foreach (var group in groups)
        {
            var filePath = group.Key;
            var subsInFile = group.ToList();

            var rawYaml = _yamlSerializer.Serialize(subsInFile);
            _fileSystem.WriteToFile(exportPath / filePath, FormatYaml(rawYaml));
        }
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

    private static string FormatYaml(string rawYaml)
    {
        var lines = rawYaml.Split([Environment.NewLine], StringSplitOptions.None);
        var modifiedLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // If this line starts a new list item (starts with "- ") and it's not the first item,
            // add an empty line before it
            if (line.StartsWith("- ") && i > 0 && modifiedLines.Count > 0)
            {
                modifiedLines.Add(string.Empty);
            }

            modifiedLines.Add(line);
        }

        return string.Join(Environment.NewLine, modifiedLines);
    }
}

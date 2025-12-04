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
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections)
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

    private async Task ProcessAndWriteYamlGroups<TData, TYaml>(
        NativePath exportPath,
        Func<Task<IEnumerable<TData>>> fetchData,
        Func<TData, TYaml> convertToYaml,
        Func<TYaml, string> getFilePath,
        string folderPath)
    {
        var data = await fetchData();
        var yamlGroups = data
            .Select(convertToYaml)
            .Select(yaml => (filePath: getFilePath(yaml), yaml))
            .GroupBy(t => t.filePath, t => t.yaml);

        _fileSystem.CreateDirectory(exportPath / folderPath);
        WriteGroupsToFiles(exportPath, yamlGroups.Cast<IGrouping<string, object>>());
    }

    private async Task ExportSubscriptions(NativePath exportPath)
    {
        await ProcessAndWriteYamlGroups<Subscription, SubscriptionYaml>(
            exportPath,
            async () => await _api.Subscriptions.ListSubscriptionsAsync(),
            sub => new SubscriptionYaml
            {
                Id = sub.Id,
                Enabled = sub.Enabled,
                Channel = sub.Channel.Name,
                SourceRepository = sub.SourceRepository,
                TargetRepository = sub.TargetRepository,
                TargetBranch = sub.TargetBranch,
                UpdateFrequency = sub.Policy.UpdateFrequency,
                Batchable = sub.Policy.Batchable,
                MergePolicies = ConvertMergePolicies(sub.Policy.MergePolicies),
                FailureNotificationTags = sub.PullRequestFailureNotificationTags,
                SourceEnabled = sub.SourceEnabled,
                SourceDirectory = sub.SourceDirectory,
                TargetDirectory = sub.TargetDirectory,
                ExcludedAssets = sub.ExcludedAssets,
            },
            MaestroConfigHelper.GetDefaultSubscriptionFilePath,
            MaestroConfigHelper.SubscriptionFolderPath);
    }

    private async Task ExportChannels(NativePath exportPath)
    {
        await ProcessAndWriteYamlGroups<Channel, ChannelYaml>(
            exportPath,
            async () => await _api.Channels.ListChannelsAsync(),
            channel => new ChannelYaml
            {
                Name = channel.Name,
                Classification = channel.Classification,
            },
            MaestroConfigHelper.GetDefaultChannelFilePath,
            MaestroConfigHelper.ChannelFolderPath);
    }

    private async Task ExportDefaultChannels(NativePath exportPath)
    {
        await ProcessAndWriteYamlGroups<DefaultChannel, DefaultChannelYaml>(
            exportPath,
            async () => await _api.DefaultChannels.ListAsync(),
            dc => new DefaultChannelYaml
            {
                Repository = dc.Repository,
                Branch = dc.Branch,
                ChannelId = dc.Channel.Id,
                Enabled = dc.Enabled,
            },
            MaestroConfigHelper.GetDefaultDefaultChannelFilePath,
            MaestroConfigHelper.DefaultChannelFolderPath);
    }

    private async Task ExportBranchMergePolicies(NativePath exportPath)
    {
        await ProcessAndWriteYamlGroups<RepositoryBranch, BranchMergePoliciesYaml>(
            exportPath,
            async () => await _api.Repository.ListRepositoriesAsync(),
            rb => new BranchMergePoliciesYaml
            {
                Repository = rb.Repository,
                Branch = rb.Branch,
                MergePolicies = ConvertMergePolicies(rb.MergePolicies),
            },
            MaestroConfigHelper.GetDefaultRepositoryBranchFilePath,
            MaestroConfigHelper.RepositoryBranchFolderPath);
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
        return rawYaml.Replace("\n-", "\n\n-");
    }
}

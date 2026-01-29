// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
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

    private void ProcessAndWriteYamlGroups<TData, TYaml>(
        NativePath exportPath,
        IEnumerable<TData> data,
        Func<TData, TYaml> convertToYaml,
        Func<TYaml, string> getFilePath,
        IComparer<TYaml> comparer,
        string folderPath)
        where TYaml : IYamlModel
    {
        var yamlGroups = data
            .Select(convertToYaml)
            .Select(yaml => (filePath: getFilePath(yaml), yaml))
            .GroupBy(t => t.filePath.ToLower(), t => t.yaml);

        _fileSystem.CreateDirectory(exportPath / folderPath);
        WriteGroupsToFiles(exportPath, yamlGroups, comparer);
    }

    private async Task ExportSubscriptions(NativePath exportPath)
    {
        var subscriptions = await _api.Subscriptions.ListSubscriptionsAsync();
        ProcessAndWriteYamlGroups(
            exportPath,
            subscriptions,
            SubscriptionYaml.FromClientModel,
            ConfigFilePathResolver.GetDefaultSubscriptionFilePath,
            new SubscriptionYamlComparer(),
            ConfigFilePathResolver.SubscriptionFolderPath);
    }

    private async Task ExportChannels(NativePath exportPath)
    {
        var channels = await _api.Channels.ListChannelsAsync();
        ProcessAndWriteYamlGroups(
            exportPath,
            channels,
            ChannelYaml.FromClientModel,
            ConfigFilePathResolver.GetDefaultChannelFilePath,
            new ChannelYamlComparer(),
            ConfigFilePathResolver.ChannelFolderPath);
    }

    private async Task ExportDefaultChannels(NativePath exportPath)
    {
        var defaultChannels = await _api.DefaultChannels.ListAsync();
        ProcessAndWriteYamlGroups(
            exportPath,
            defaultChannels,
            DefaultChannelYaml.FromClientModel,
            ConfigFilePathResolver.GetDefaultDefaultChannelFilePath,
            new DefaultChannelYamlComparer(),
            ConfigFilePathResolver.DefaultChannelFolderPath);
    }

    private async Task ExportBranchMergePolicies(NativePath exportPath)
    {
        var repositoryBranches = (await _api.Repository.ListRepositoriesAsync())
            .Where(rb => rb.MergePolicies.Any());
        ProcessAndWriteYamlGroups(
            exportPath,
            repositoryBranches,
            BranchMergePoliciesYaml.FromClientModel,
            ConfigFilePathResolver.GetDefaultRepositoryBranchFilePath,
            new BranchMergePoliciesYamlComparer(),
            ConfigFilePathResolver.RepositoryBranchFolderPath);
    }

    private void WriteGroupsToFiles<TYaml>(NativePath exportPath, IEnumerable<IGrouping<string, TYaml>> groups, IComparer<TYaml> comparer)
        where TYaml : IYamlModel
    {
        foreach (var group in groups)
        {
            var filePath = group.Key;
            var configurationObjects = group.OrderBy(x => x, comparer).ToList();
            
            var rawYaml = _yamlSerializer.Serialize(configurationObjects);
            _fileSystem.WriteToFile(exportPath / filePath, FormatYaml(rawYaml));
        }
    }

    private static string FormatYaml(string rawYaml)
    {
        return rawYaml.Replace("\n-", "\n\n-");
    }
}

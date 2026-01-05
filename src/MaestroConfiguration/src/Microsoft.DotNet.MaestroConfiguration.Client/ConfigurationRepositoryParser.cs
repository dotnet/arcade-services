// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using YamlDotNet.Serialization;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

public interface IConfigurationRepositoryParser
{
    Task<YamlConfiguration> ParseAsync(string repoUri, string branch);
}

public class ConfigurationRepositoryParser : IConfigurationRepositoryParser
{
    IGitRepoFactory _gitRepoFactory;
    public ConfigurationRepositoryParser(IGitRepoFactory gitRepoFactory)
    {
        _gitRepoFactory = gitRepoFactory;
    }

    public async Task<YamlConfiguration> ParseAsync(string repoUri, string branch)
    {
        var gitRepo = await _gitRepoFactory.CreateClient(repoUri);

        var deserializer = new DeserializerBuilder().Build();

        var subscriptions = (await gitRepo.GetFilesContentAsync(repoUri, branch, ConfigFilePathResolver.SubscriptionFolderPath))
            .SelectMany(f => deserializer.Deserialize<List<SubscriptionYaml>>(f.Content) ?? [])
            .ToList();

        var channels = (await gitRepo.GetFilesContentAsync(repoUri, branch, ConfigFilePathResolver.ChannelFolderPath))
            .SelectMany(f => deserializer.Deserialize<List<ChannelYaml>>(f.Content) ?? [])
            .ToList() ?? [];

        var defaultChannels = (await gitRepo.GetFilesContentAsync(repoUri, branch, ConfigFilePathResolver.DefaultChannelFolderPath))
            .SelectMany(f => deserializer.Deserialize<List<DefaultChannelYaml>>(f.Content) ?? [])
            .ToList() ?? [];

        var branchMergePolicies = (await gitRepo.GetFilesContentAsync(repoUri, branch, ConfigFilePathResolver.RepositoryBranchFolderPath))
            .SelectMany(f => deserializer.Deserialize<List<BranchMergePoliciesYaml>>(f.Content) ?? [])
            .ToList() ?? [];

        return new YamlConfiguration(
            subscriptions,
            channels,
            defaultChannels,
            branchMergePolicies);
    }
}

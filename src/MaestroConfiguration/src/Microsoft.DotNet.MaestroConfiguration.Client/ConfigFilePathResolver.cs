// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using Maestro.Common;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client.Helpers;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

public static class ConfigFilePathResolver
{
    private static string ConfigurationFolderPath = new("configuration");
    private const string SubscriptionFolder = "subscriptions";
    private const string ChannelFolder = "channels";
    private const string DefaultChannelFolder = "default-channels";
    private const string RepositoryBranchFolder = "branch-merge-policies";
    private const string YamlFileExtension = ".yml";

    public static string SubscriptionFolderPath => Path.Combine(ConfigurationFolderPath, SubscriptionFolder);
    public static string ChannelFolderPath => Path.Combine(ConfigurationFolderPath, ChannelFolder);
    public static string DefaultChannelFolderPath => Path.Combine(ConfigurationFolderPath, DefaultChannelFolder);
    public static string RepositoryBranchFolderPath => Path.Combine(ConfigurationFolderPath, RepositoryBranchFolder);

    public static string GetDefaultSubscriptionFilePath(SubscriptionYaml subscription) =>
        Path.Combine(SubscriptionFolderPath, GetFileNameBasedOnRepo(subscription.TargetRepository));

    public static string GetDefaultChannelFilePath(ChannelYaml channel) =>
        NormalizeChannelName(Path.Combine(ChannelFolderPath, (ChannelCategorizer.CategorizeChannels([new Channel(0, channel.Name, string.Empty)]).First().Name + YamlFileExtension)));

    public static string GetDefaultDefaultChannelFilePath(DefaultChannelYaml defaultChannel) =>
        Path.Combine(DefaultChannelFolderPath, GetFileNameBasedOnRepo(defaultChannel.Repository));

    public static string GetDefaultRepositoryBranchFilePath(BranchMergePoliciesYaml branchMergePolicies) =>
        Path.Combine(RepositoryBranchFolderPath, GetFileNameBasedOnRepo(branchMergePolicies.Repository));

    private static string GetFileNameBasedOnRepo(string repository)
    {
        try
        {
            var (repoName, owner) = GitRepoUrlUtils.GetRepoNameAndOwner(repository);
            return $"{owner}-{repoName}{YamlFileExtension}";
        }
        catch (ArgumentException)
        {
            if (GitRepoUrlUtils.ParseTypeFromUri(repository) == GitRepoType.AzureDevOps)
            {
                return repository.Split('/', StringSplitOptions.RemoveEmptyEntries).Last() + YamlFileExtension;
            }
            else
            {
                throw;
            }
        }
    }

    private static string NormalizeChannelName(string channelName) =>
        channelName.ToLowerInvariant().Replace(" ", "-");
}

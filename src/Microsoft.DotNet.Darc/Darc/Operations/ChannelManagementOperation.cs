// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc.Yaml;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

internal abstract class ChannelManagementOperation : ConfigurationManagementOperation
{
    private readonly ILogger _logger;

    protected ChannelManagementOperation(
            IConfigurationManagementCommandLineOptions options,
            IGitRepoFactory gitRepoFactory,
            IRemoteFactory remoteFactory,
            ILogger logger,
            ILocalGitRepoFactory localGitRepoFactory)
        : base(options, gitRepoFactory, remoteFactory, logger, localGitRepoFactory)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets all channels from all category files.
    /// </summary>
    /// <param name="branch">The branch to read from</param>
    /// <returns>List of all channels with their category information</returns>
    protected async Task<List<ChannelWithCategory>> GetAllChannelsFromCategoryFiles(string? branch = null)
    {
        var allChannels = new List<ChannelWithCategory>();

        foreach (var categoryFilePath in ChannelFileManager.GetAllCategoryFilePaths())
        {
            try
            {
                var channels = await GetConfiguration<ChannelYamlData>(categoryFilePath, branch);
                var category = GetCategoryFromFilePath(categoryFilePath);

                foreach (var channel in channels)
                {
                    allChannels.Add(new ChannelWithCategory
                    {
                        Channel = channel,
                        Category = category,
                        FilePath = categoryFilePath
                    });
                }
            }
            catch (DependencyFileNotFoundException)
            {
                // Category file doesn't exist yet, which is fine
                continue;
            }
        }

        return allChannels;
    }

    /// <summary>
    /// Finds a channel by name across all category files.
    /// </summary>
    /// <param name="channelName">The name of the channel to find</param>
    /// <param name="branch">The branch to search in</param>
    /// <returns>The channel with its category information if found, null otherwise</returns>
    protected async Task<ChannelWithCategory?> FindChannelInCategoryFiles(string channelName, string? branch = null)
    {
        var allChannels = await GetAllChannelsFromCategoryFiles(branch);
        return allChannels.FirstOrDefault(c => c.Channel.Name == channelName);
    }

    /// <summary>
    /// Adds a channel to the appropriate category file.
    /// </summary>
    /// <param name="channel">The channel to add</param>
    /// <param name="commitMessage">The commit message</param>
    protected async Task AddChannelToCategoryFile(ChannelYamlData channel, string commitMessage)
    {
        var category = ChannelFileManager.DetermineCategoryForChannel(channel.Name, channel.Classification);
        var filePath = ChannelFileManager.GetCategoryFilePath(category);

        var existingChannels = await GetConfiguration<ChannelYamlData>(filePath);
        existingChannels.Add(channel);

        // Sort channels by name for consistency
        existingChannels = existingChannels.OrderBy(c => c.Name).ToList();

        _logger.LogInformation("Adding channel '{channelName}' to {fileName}", channel.Name, filePath);
        await WriteConfigurationFile(filePath, existingChannels, commitMessage);
    }

    /// <summary>
    /// Removes a channel from its category file.
    /// </summary>
    /// <param name="channelWithCategory">The channel with category information to remove</param>
    /// <param name="commitMessage">The commit message</param>
    protected async Task RemoveChannelFromCategoryFile(ChannelWithCategory channelWithCategory, string commitMessage)
    {
        var existingChannels = await GetConfiguration<ChannelYamlData>(channelWithCategory.FilePath);
        var channelToRemove = existingChannels.FirstOrDefault(c => c.Name == channelWithCategory.Channel.Name);

        if (channelToRemove != null)
        {
            existingChannels.Remove(channelToRemove);

            if (existingChannels.Any())
            {
                _logger.LogInformation("Removing channel '{channelName}' from {fileName}", channelWithCategory.Channel.Name, channelWithCategory.FilePath);
                await WriteConfigurationFile(channelWithCategory.FilePath, existingChannels, commitMessage);
            }
            else
            {
                // If no channels left in the category, remove the file
                _logger.LogInformation("Removing empty category file {fileName}", channelWithCategory.FilePath);
                await RemoveConfigurationFile(channelWithCategory.FilePath);
            }
        }
    }

    /// <summary>
    /// Extracts the category name from a file path.
    /// </summary>
    /// <param name="filePath">The file path</param>
    /// <returns>The category name</returns>
    private static string GetCategoryFromFilePath(UnixPath filePath)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath.Path);

        // Reverse the sanitization process
        var category = fileName
            .Replace("-", " ")
            .Replace("dotnet", ".NET");

        // Handle special cases
        if (category.StartsWith(".NET ") && char.IsDigit(category[5]))
        {
            return category; // .NET 6, .NET 7, etc.
        }
        else if (category == ".NET")
        {
            return ".NET";
        }
        else if (category.ToLowerInvariant() == "vs")
        {
            return "VS";
        }
        else if (category.ToLowerInvariant() == "windows")
        {
            return "Windows";
        }
        else if (category.ToLowerInvariant() == "other")
        {
            return "Other";
        }
        else if (category.ToLowerInvariant() == "test")
        {
            return "Test";
        }

        // Fallback to title case
        return char.ToUpper(category[0]) + category.Substring(1).ToLower();
    }

    /// <summary>
    /// Represents a channel with its category information.
    /// </summary>
    protected class ChannelWithCategory
    {
        public ChannelYamlData Channel { get; set; } = null!;
        public string Category { get; set; } = null!;
        public UnixPath FilePath { get; set; } = default!;
    }
}

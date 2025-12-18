// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

public class ConfigurationRepositoryManager : IConfigurationRepositoryManager
{
    private readonly IGitRepoFactory _configurationRepoFactory;
    private readonly ILogger<IConfigurationRepositoryManager> _logger;

    private static readonly ISerializer _yamlSerializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .Build();

    public ConfigurationRepositoryManager(
        IGitRepoFactory configurationRepoFactory,
        ILogger<IConfigurationRepositoryManager> logger)
    {
        _logger = logger;
        _configurationRepoFactory = configurationRepoFactory;
    }

    public async Task AddSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml subscription)
        => await PerformConfigurationRepositoryOperationInternal(
            parameters,
            subscription,
            (p, repo, branch, s) => AddModelInternalAsync<SubscriptionYaml, Guid>(
                p, repo, branch, s,
                ConfigFilePathResolver.GetDefaultSubscriptionFilePath,
                (existing, newSub) => existing.IsEquivalentTo(newSub),
                (existing, filePath) => $"Subscription {existing.Id} with equivalent parameters already exists in '{filePath}'.",
                new SubscriptionYamlComparer(),
                $"Add new subscription ({s.Channel}) {s.SourceRepository} => {s.TargetRepository} ({s.TargetBranch})"),
            $"Successfully added subscription with id '{subscription.Id}' on branch '{parameters.ConfigurationBranch}' of the configuration repository {parameters.RepositoryUri}");

    public async Task DeleteSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml subscription)
        => await PerformConfigurationRepositoryOperationInternal(
            parameters,
            subscription,
            (p, repo, branch, s) => DeleteModelInternalAsync<SubscriptionYaml, Guid>(
                p, repo, branch, s,
                YamlModelUniqueKeys.GetSubscriptionKey,
                (model, filePath, repoUri, branchName) => $"Found no subscription with id {model.Id} to delete in file {filePath} of repo {repoUri} on branch {branchName}",
                new SubscriptionYamlComparer(),
                $"Delete subscription {s.Id}"),
            $"Successfully deleted subscription with id '{subscription.Id}' from branch '{parameters.ConfigurationBranch}' of the configuration repository {parameters.RepositoryUri}");

    public async Task UpdateSubscriptionAsync(ConfigurationRepositoryOperationParameters parameters, SubscriptionYaml updatedSubscription)
        => await PerformConfigurationRepositoryOperationInternal(
            parameters,
            updatedSubscription,
            (p, repo, branch, s) => UpdateModelInternalAsync<SubscriptionYaml, Guid>(
                p, repo, branch, s,
                YamlModelUniqueKeys.GetSubscriptionKey,
                (model, filePath, repoUri, branchName) => $"No existing subscription with id {model.Id} found in file {filePath} of repo {repoUri} on branch {branchName}",
                new SubscriptionYamlComparer(),
                $"Update subscription {s.Id}"),
            $"Successfully updated subscription with id '{updatedSubscription.Id}' on branch '{parameters.ConfigurationBranch}' of the configuration repository {parameters.RepositoryUri}");

    public async Task AddChannelAsync(ConfigurationRepositoryOperationParameters parameters, ChannelYaml channel)
        => await PerformConfigurationRepositoryOperationInternal(
            parameters,
            channel,
            (p, repo, branch, c) => AddModelInternalAsync<ChannelYaml, string>(
                p, repo, branch, c,
                ConfigFilePathResolver.GetDefaultChannelFilePath,
                (existing, newChannel) => string.Equals(existing.Name, newChannel.Name, StringComparison.OrdinalIgnoreCase),
                (existing, filePath) => $"Channel with name '{existing.Name}' already exists in '{filePath}'.",
                new ChannelYamlComparer(),
                $"Add new channel '{c.Name}'"),
            $"Successfully added channel '{channel.Name}' on branch '{parameters.ConfigurationBranch}' of the configuration repository {parameters.RepositoryUri}");


    private async Task PerformConfigurationRepositoryOperationInternal<T>(
        ConfigurationRepositoryOperationParameters parameters,
        T yamlModel,
        Func<ConfigurationRepositoryOperationParameters, IGitRepo, string, T, Task> operation,
        string noPrOperationMessage)
        where T : IYamlModel
    {
        var configurationRepo = await _configurationRepoFactory.CreateClient(parameters.RepositoryUri);

        await ValidateConfigurationRepositoryParametersAsync(configurationRepo, parameters);
        var workingBranch = await PrepareConfigurationBranchAsync(configurationRepo, parameters);

        await operation(parameters, configurationRepo, workingBranch, yamlModel);

        if (!parameters.DontOpenPr)
        {
            await CreatePullRequest(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                parameters.ConfigurationBaseBranch,
                "Updating Maestro configuration");
        }
        else
        {
            _logger.LogInformation("{message}", noPrOperationMessage);
        }
    }

    /// <summary>
    /// Generic method to add a new configuration object to the configuration repository.
    /// </summary>
    private async Task AddModelInternalAsync<T, TKey>(
        ConfigurationRepositoryOperationParameters parameters,
        IGitRepo configurationRepo,
        string workingBranch,
        T yamlModel,
        Func<T, string> getDefaultFilePath,
        Func<T, T, bool> isEquivalent,
        Func<T, string, string> getDuplicateError,
        IComparer<T> comparer,
        string commitMessage)
        where T : IYamlModel
        where TKey : IEquatable<TKey>
    {
        var filePath = string.IsNullOrEmpty(parameters.ConfigurationFilePath)
            ? getDefaultFilePath(yamlModel)
            : parameters.ConfigurationFilePath;
        _logger.LogInformation("Adding new configuration to file {0}", filePath);

        var yamlModelsInFile = await FetchAndParseRemoteConfiguration<T>(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            filePath);

        var equivalentInFile = yamlModelsInFile.FirstOrDefault(m => isEquivalent(m, yamlModel));
        if (equivalentInFile != null)
        {
            throw new ArgumentException(getDuplicateError(equivalentInFile, filePath));
        }

        yamlModelsInFile.Add(yamlModel);
        await CommitConfigurationDataAsync(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            filePath,
            yamlModelsInFile,
            comparer,
            commitMessage);
    }

    /// <summary>
    /// Generic method to delete a configuration object from the configuration repository.
    /// </summary>
    private async Task DeleteModelInternalAsync<T, TKey>(
        ConfigurationRepositoryOperationParameters parameters,
        IGitRepo configurationRepo,
        string workingBranch,
        T yamlModel,
        Func<T, TKey> getKey,
        Func<T, string, string, string, string> getNotFoundError,
        IComparer<T> comparer,
        string commitMessage)
        where T : IYamlModel
        where TKey : IEquatable<TKey>
    {
        var (filePath, yamlModelsInFile) = await GetFilePathAndModels(
            parameters,
            configurationRepo,
            workingBranch,
            yamlModel,
            getKey);

        var yamlModelKey = getKey(yamlModel);
        var yamlModelsWithoutDeleted = yamlModelsInFile.Where(m => !getKey(m).Equals(yamlModelKey)).ToList();

        if (yamlModelsInFile.Count == yamlModelsWithoutDeleted.Count)
        {
            throw new ArgumentException(getNotFoundError(
                yamlModel,
                filePath,
                parameters.RepositoryUri,
                parameters.ConfigurationBranch ?? parameters.ConfigurationBaseBranch));
        }

        await CommitConfigurationDataAsync(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            filePath,
            yamlModelsWithoutDeleted,
            comparer,
            commitMessage);
    }

    /// <summary>
    /// Generic method to update an existing configuration object in the configuration repository.
    /// </summary>
    private async Task UpdateModelInternalAsync<T, TKey>(
        ConfigurationRepositoryOperationParameters parameters,
        IGitRepo configurationRepo,
        string workingBranch,
        T updatedYamlModel,
        Func<T, TKey> getKey,
        Func<T, string, string, string, string> getNotFoundError,
        IComparer<T> comparer,
        string commitMessage)
        where T : IYamlModel
        where TKey : IEquatable<TKey>
    {
        var (filePath, yamlModelsInFile) = await GetFilePathAndModels(
            parameters,
            configurationRepo,
            workingBranch,
            updatedYamlModel,
            getKey);

        var yamlModelKey = getKey(updatedYamlModel);
        var existingYamlModel = yamlModelsInFile.FirstOrDefault(m => getKey(m).Equals(yamlModelKey));
        if (existingYamlModel == null)
        {
            throw new ArgumentException(getNotFoundError(
                updatedYamlModel,
                filePath,
                parameters.RepositoryUri,
                parameters.ConfigurationBranch ?? parameters.ConfigurationBaseBranch));
        }

        var index = yamlModelsInFile.IndexOf(existingYamlModel);
        yamlModelsInFile[index] = updatedYamlModel;

        await CommitConfigurationDataAsync(
            configurationRepo,
            parameters.RepositoryUri,
            workingBranch,
            filePath,
            yamlModelsInFile,
            comparer,
            commitMessage);
    }

    /// <summary>
    /// Gets the file path and yaml models, either from the specified path or by searching.
    /// </summary>
    private async Task<(string FilePath, List<T> YamlModels)> GetFilePathAndModels<T, TKey>(
        ConfigurationRepositoryOperationParameters parameters,
        IGitRepo configurationRepo,
        string workingBranch,
        T yamlModel,
        Func<T, TKey> getKey)
        where T : IYamlModel
        where TKey : IEquatable<TKey>
    {
        if (string.IsNullOrEmpty(parameters.ConfigurationFilePath))
        {
            return await FindAndParseConfigurationFile(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                yamlModel,
                getKey);
        }
        else
        {
            var yamlModels = await FetchAndParseRemoteConfiguration<T>(
                configurationRepo,
                parameters.RepositoryUri,
                workingBranch,
                parameters.ConfigurationFilePath);
            return (parameters.ConfigurationFilePath, yamlModels);
        }
    }

    #region helper methods
    private static async Task ValidateConfigurationRepositoryParametersAsync(
        IGitRepo gitRepo,
        ConfigurationRepositoryOperationParameters operationParameters)
    {
        if (!await gitRepo.RepoExistsAsync(operationParameters.RepositoryUri))
        {
            throw new ArgumentException($"The configuration repository '{operationParameters.RepositoryUri}' is not a valid git repository.");
        }

        if (!await gitRepo.DoesBranchExistAsync(operationParameters.RepositoryUri, operationParameters.ConfigurationBaseBranch))
        {
            throw new ArgumentException($"The configuration base branch '{operationParameters.ConfigurationBaseBranch}' does not exist in the repository '{operationParameters.RepositoryUri}'.");
        }
    }

    private static async Task CommitConfigurationDataAsync<T>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        string filePath,
        IEnumerable<T> data,
        IComparer<T> comparer,
        string commitMessage)
        where T : IYamlModel
    {
        if (!data.Any())
        {
            await gitRepo.DeleteFileAsync(repositoryUri, workingBranch, filePath, commitMessage);
        }
        else
        {
            string yamlContent = _yamlSerializer.Serialize(data.OrderBy(x => x, comparer)).Replace("\n-", "\n\n-");
            await gitRepo.CommitFilesAsync(repositoryUri, workingBranch, [new GitFile(filePath, yamlContent)], commitMessage);
        }
    }

    /// <summary>
    /// Ensures that a configuration working branch exists, creating one if necessary.
    /// </summary>
    private static async Task<string> PrepareConfigurationBranchAsync(
        IGitRepo gitRepo,
        ConfigurationRepositoryOperationParameters parameters)
    {
        if (string.IsNullOrEmpty(parameters.ConfigurationBranch))
        {
            var branch = $"darc/{parameters.ConfigurationBaseBranch}-{Guid.NewGuid().ToString().Substring(0, 8)}";
            await gitRepo.CreateBranchAsync(
                parameters.RepositoryUri,
                branch,
                parameters.ConfigurationBaseBranch);
            return branch;
        }
        else
        {
            if (!await gitRepo.DoesBranchExistAsync(parameters.RepositoryUri, parameters.ConfigurationBranch))
            {
                await gitRepo.CreateBranchAsync(
                    parameters.RepositoryUri,
                    parameters.ConfigurationBranch,
                    parameters.ConfigurationBaseBranch);
            }
            return parameters.ConfigurationBranch;
        }
    }

    private static async Task<List<TData>> FetchAndParseRemoteConfiguration<TData>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        string filePath)
    {
        string fileContents;

        try
        {
            fileContents = await gitRepo.GetFileContentsAsync(
                repositoryUri,
                workingBranch,
                filePath);
            return _yamlDeserializer.Deserialize<List<TData>>(fileContents);
        }
        catch (FileNotFoundInRepoException)
        {
            return [];
        }   
    }

    private async Task CreatePullRequest(
        IGitRepo gitRepo,
        string repositoryUri,
        string headBranch,
        string targetBranch,
        string title,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentException.ThrowIfNullOrEmpty(headBranch);
        ArgumentException.ThrowIfNullOrEmpty(targetBranch);

        _logger.LogInformation("Creating pull request from {0} to {1}...", headBranch, targetBranch);
        var prUrl = await gitRepo.CreatePullRequestAsync(
            repositoryUri,
            headBranch,
            targetBranch,
            title,
            description);
        var prId = prUrl.Substring(prUrl.LastIndexOf('/') + 1);
        var guiUri = $"{repositoryUri}/pullrequest/{prId}";
        _logger.LogInformation("Created pull request at {0}", guiUri);
    }

    private async Task<(string, List<T>)> FindAndParseConfigurationFile<T, TKey>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        T searchObject,
        Func<T, TKey> getUniqueKey)
        where T : IYamlModel
        where TKey : IEquatable<TKey>
    {
        // Try the default file first before searching all files
        _logger.LogInformation("No configuration file path provided. Trying default location first...");
        var result = await TryFindInDefaultFileAsync(gitRepo, repositoryUri, workingBranch, searchObject, getUniqueKey);
        if (result.HasValue)
        {
            return result.Value;
        }

        // If not in the default file, search all files in the folder for that yaml type
        _logger.LogInformation("Couldn't find configuration object at the default location. Searching all files in folder... this might take a few minutes");
        return await SearchAllFilesInFolderAsync(gitRepo, repositoryUri, workingBranch, searchObject, getUniqueKey);
    }

    private async Task<(string FilePath, List<T> Content)?> TryFindInDefaultFileAsync<T, TKey>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        T searchObject,
        Func<T, TKey> getUniqueKey)
        where T : IYamlModel
        where TKey : IEquatable<TKey>
    {
        var defaultFilePath = ConfigFilePathResolver.GetDefaultFilePath(searchObject);
        var searchKey = getUniqueKey(searchObject);

        try
        {
            var defaultFileContents = await gitRepo.GetFileContentsAsync(repositoryUri, workingBranch, defaultFilePath);
            var deserializedYamls = _yamlDeserializer.Deserialize<List<T>>(defaultFileContents);

            if (deserializedYamls.Any(y => searchKey.Equals(getUniqueKey(y))))
            {
                return (defaultFilePath, deserializedYamls);
            }
        }
        catch (FileNotFoundInRepoException)
        {
            // Default file doesn't exist
        }

        return null;
    }

    private async Task<(string FilePath, List<T> Content)> SearchAllFilesInFolderAsync<T, TKey>(
        IGitRepo gitRepo,
        string repositoryUri,
        string workingBranch,
        T searchObject,
        Func<T, TKey> getUniqueKey)
        where T : IYamlModel
        where TKey : IEquatable<TKey>
    {
        var folderPath = ConfigFilePathResolver.GetDefaultFileFolder(searchObject);
        var searchKey = getUniqueKey(searchObject);

        // Get list of all files in the folder
        var filePaths = await gitRepo.ListBlobsAsync(repositoryUri, workingBranch, folderPath);

        // Search each file one by one for the object
        foreach (var filePath in filePaths)
        {
            try
            {
                var fileContent = await gitRepo.GetFileContentsAsync(repositoryUri, workingBranch, filePath);
                var deserializedYamls = _yamlDeserializer.Deserialize<List<T>>(fileContent);

                if (deserializedYamls.Any(y => searchKey.Equals(getUniqueKey(y))))
                {
                    _logger.LogInformation("Object with key {0} found in {1}", searchKey, filePath);
                    return (filePath, deserializedYamls);
                }
            }
            catch (FileNotFoundInRepoException)
            {
                // File was listed but couldn't be read, skip it
                continue;
            }
        }

        throw new ArgumentException($"No object with key {searchKey} was found on branch {workingBranch}");
    }
    #endregion
}
